using CONVERTit.Core.Catalog;
using CONVERTit.Core.Ffmpeg;
using CONVERTit.Core.Files;
using CONVERTit.Core.Jobs;
using CONVERTit.Core.Media;
using CONVERTit.Core.Options;
using CONVERTit.Core.Storage;
using CONVERTit.Engines.Ffmpeg;
using CONVERTit.Engines.Image;

namespace CONVERTit.App.Services;

/// <summary>A file to convert; Root is the folder of a folder batch (mirrored structure).</summary>
public sealed record SourceRef(string Path, string? Root);

/// <summary>
/// Plans jobs for every page: output path (with conflicts), options snapshot and the work itself.
/// Every job writes to a temporary file next to the target and renames it when successful, so an aborted
/// conversion never leaves a half-written file under the final name and the source is never touched.
/// </summary>
public sealed class ConversionService(FfmpegRunner runner, MediaProbe probe)
{
    /// <summary>Optional modules (upscalers, RIFE); set by the main view model.</summary>
    public ModuleLocator? Modules { get; set; }

    /// <summary>Hardware decoding, GPU turbo, threads and picture corrections of the current performance profile.</summary>
    public Func<EncodeEnvironment> EncodeSettings { get; set; } = () => EncodeEnvironment.Default;

    // ================================================================ Video

    public List<ConversionJob> VideoConvert(IReadOnlyList<SourceRef> sources, VideoOptions videoOptions, OutputOptions outputOptions, bool skipSame)
    {
        var video = JsonFile.Clone(videoOptions);
        var output = JsonFile.Clone(outputOptions);
        var resolver = new OutputPathResolver(output);
        var ext = FormatCatalog.Extension(video.Container);
        var batch = Guid.NewGuid();
        return sources.Select(s =>
        {
            var plan = resolver.Plan(s.Path, s.Root, ext);
            return MediaJob(s, plan, output, MediaKind.Video, "OpConvertVideo", batch, async (ctx, info, temp) =>
            {
                if (skipSame && MediaTypes.Extension(s.Path) == ext && FormatCatalog.CodecFromFfmpeg(info.MainVideo?.Codec) == video.Codec)
                {
                    throw new JobSkippedException("SkipSameFormat");
                }

                if (video.QualityMode == VideoQualityMode.TargetSize)
                {
                    await RunTargetSizeAsync(env => FfmpegCommands.Video(info, s.Path, temp, video, environment: env),
                        CompressPlanner.ResolveTargetSize(info, video), plan, ctx);
                }
                else
                {
                    await RunVideoAsync(env => FfmpegCommands.Video(info, s.Path, temp, video, environment: env), ctx);
                }
            });
        }).ToList();
    }

    /// <summary>
    /// Bitrate encodes for a size: two passes with x264, x265 and VP9 (close to the size); NVENC runs its two passes
    /// inside one encode (-multipass), the other hardware encoders and SVT-AV1 use one pass.
    /// </summary>
    private async Task RunTargetSizeAsync(Func<EncodeEnvironment, FfmpegCommand> build, VideoOptions resolved, OutputPlan plan, JobContext ctx)
    {
        var encoder = FormatCatalog.VideoEncoder(resolved.Codec, resolved.Acceleration) ?? FormatCatalog.VideoEncoder(resolved.Codec, Acceleration.Off)!;
        if (resolved.QualityMode != VideoQualityMode.Bitrate || !FfmpegCommands.SupportsTwoPass(encoder))
        {
            await RunVideoAsync(build, ctx);
            return;
        }

        // Statistics files are removed with the job.
        var log = OutputPathResolver.TempPathFor(plan.Path, "pass");
        ctx.AddTemp(log + "-0.log");
        ctx.AddTemp(log + "-0.log.mbtree");
        var x265 = Path.Combine(Path.GetDirectoryName(log) ?? "", FfmpegCommands.X265StatsName(log));
        foreach (var file in new[] { x265, x265 + ".cutree", x265 + ".temp", x265 + ".cutree.temp" })
        {
            ctx.AddTemp(file);
        }

        await RunVideoAsync(build, ctx, async command =>
        {
            var (pass1, pass2) = FfmpegCommands.TwoPass(command, log);
            await RunAsync(pass1, ctx, 0, 0.4);
            await RunAsync(pass2, ctx, 0.4, 1);
        });
    }

    /// <summary>
    /// Runs a video encode with the current encode settings. When the GPU path fails (driver, unsupported profile),
    /// the job is repeated without GPU turbo, then without hardware decoding – the result is the same, only slower.
    /// </summary>
    private async Task RunVideoAsync(Func<EncodeEnvironment, FfmpegCommand> build, JobContext ctx,
        Func<FfmpegCommand, Task>? run = null, double from = 0, double to = 1)
    {
        var env = EncodeSettings();
        while (true)
        {
            var command = build(env);
            try
            {
                await (run is null ? RunAsync(command, ctx, from, to) : run(command));
                return;
            }
            catch (FfmpegException) when (!ctx.CancellationToken.IsCancellationRequested && (command.UsesTurbo || command.UsesHardwareDecode))
            {
                ctx.Warn(command.UsesTurbo ? "WarnTurboFallback" : "WarnHwDecodeFallback");
                env = command.UsesTurbo ? env with { GpuTurbo = false } : env with { HardwareDecode = false };
            }
        }
    }

    public List<ConversionJob> Animation(IReadOnlyList<SourceRef> sources, GifOptions gifOptions, OutputOptions outputOptions)
    {
        var gif = JsonFile.Clone(gifOptions);
        var output = JsonFile.Clone(outputOptions);
        var resolver = new OutputPathResolver(output);
        var ext = gif.Format == AnimationFormat.Gif ? "gif" : "webp";
        var batch = Guid.NewGuid();
        return sources.Select(s => MediaJob(s, resolver.Plan(s.Path, s.Root, ext), output, MediaKind.Video,
            gif.Format == AnimationFormat.Gif ? "OpGif" : "OpWebP", batch,
            (ctx, info, temp) => RunAsync(FfmpegCommands.Animation(info, s.Path, temp, gif), ctx))).ToList();
    }

    public List<ConversionJob> Remux(IReadOnlyList<SourceRef> sources, RemuxOptions remuxOptions, OutputOptions outputOptions, bool skipSame)
    {
        var remux = JsonFile.Clone(remuxOptions);
        var output = JsonFile.Clone(outputOptions);
        var resolver = new OutputPathResolver(output);
        var ext = FormatCatalog.Extension(remux.Container);
        var batch = Guid.NewGuid();
        return sources.Select(s => MediaJob(s, resolver.Plan(s.Path, s.Root, ext), output, MediaKind.Video, "OpRemux", batch,
            async (ctx, info, temp) =>
            {
                if (skipSame && MediaTypes.Extension(s.Path) == ext)
                {
                    throw new JobSkippedException("SkipSameFormat");
                }

                await RunAsync(FfmpegCommands.Remux(info, s.Path, temp, remux), ctx);
            })).ToList();
    }

    /// <summary>Audio track(s) of videos: copied as they are or converted. Needs a probe for the track list.</summary>
    public async Task<List<ConversionJob>> ExtractAudioAsync(IReadOnlyList<SourceRef> sources, ExtractAudioOptions extractOptions,
        AudioOptions audioOptions, OutputOptions outputOptions, CancellationToken cancellationToken)
    {
        var extract = JsonFile.Clone(extractOptions);
        var audio = JsonFile.Clone(audioOptions);
        var output = JsonFile.Clone(outputOptions);
        var resolver = new OutputPathResolver(output);
        var batch = Guid.NewGuid();
        var jobs = new List<ConversionJob>();
        foreach (var s in sources)
        {
            MediaInfo? info;
            try
            {
                info = await probe.GetAsync(s.Path, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                info = null;
            }

            List<AudioStreamInfo> tracks = info is null || info.Audio.Count == 0 ? [] : extract.AllTracks ? info.Audio : [info.Audio[0]];
            if (tracks.Count == 0)
            {
                var ext0 = extract.Mode == ExtractMode.Copy ? "mka" : FormatCatalog.Extension(audio.Format);
                jobs.Add(MediaJob(s, resolver.Plan(s.Path, s.Root, ext0), output, MediaKind.Audio, "OpExtractAudio", batch,
                    (_, _, _) => throw new ConversionException("ErrNoAudioStream")));
                continue;
            }

            for (var i = 0; i < tracks.Count; i++)
            {
                var track = tracks[i];
                var ext = extract.Mode == ExtractMode.Copy ? FormatCatalog.CopyExtension(track.Codec) : FormatCatalog.Extension(audio.Format);
                var suffix = tracks.Count > 1 ? $"_track{i + 1}" + (track.Language.Length > 0 ? "_" + track.Language : "") : "";
                var index = track.Index;
                jobs.Add(MediaJob(s, resolver.Plan(s.Path, s.Root, ext, suffix), output, MediaKind.Audio, "OpExtractAudio", batch,
                    (ctx, mi, temp) => RunAsync(extract.Mode == ExtractMode.Copy
                        ? FfmpegCommands.AudioCopy(mi, s.Path, temp, index, null)
                        : FfmpegCommands.Audio(mi, s.Path, temp, audio, streamIndex: index), ctx)));
            }
        }

        return jobs;
    }

    // ================================================================ Audio

    public List<ConversionJob> AudioConvert(IReadOnlyList<SourceRef> sources, AudioOptions audioOptions, OutputOptions outputOptions, bool skipSame)
    {
        var audio = JsonFile.Clone(audioOptions);
        var output = JsonFile.Clone(outputOptions);
        var resolver = new OutputPathResolver(output);
        var ext = FormatCatalog.Extension(audio.Format);
        var batch = Guid.NewGuid();
        return sources.Select(s => MediaJob(s, resolver.Plan(s.Path, s.Root, ext), output, MediaKind.Audio, "OpConvertAudio", batch,
            async (ctx, info, temp) =>
            {
                if (skipSame && FormatCatalog.SameAudioType(MediaTypes.Extension(s.Path), audio.Format))
                {
                    throw new JobSkippedException("SkipSameFormat");
                }

                await RunAsync(FfmpegCommands.Audio(info, s.Path, temp, audio), ctx);
            })).ToList();
    }

    // ================================================================ Trim & loop (video and audio)

    public List<ConversionJob> Trim(SourceRef source, MediaKind kind, MediaInfo info, TrimOptions trimOptions, VideoOptions videoConvert,
        AudioOptions audioConvert, OutputOptions outputOptions)
    {
        var trim = JsonFile.Clone(trimOptions);
        var video = JsonFile.Clone(videoConvert);
        var audio = JsonFile.Clone(audioConvert);
        var output = JsonFile.Clone(outputOptions);
        if (kind != MediaKind.Video && trim.Output is TrimOutput.Gif or TrimOutput.AnimatedWebP)
        {
            trim.Output = TrimOutput.SameFormat;
        }

        // Multi-cut: the kept segments replace the start/end marks.
        if (trim.Segments.Count > 0)
        {
            var ranges = CutList.KeptRanges(trim.Segments, info.DurationSeconds);
            if (ranges.Count == 0)
            {
                throw new ConversionException("ErrNothingKept");
            }

            if (trim.SegmentsSeparate && ranges.Count > 1)
            {
                // Every kept segment as its own file: name_part1, name_part2 …
                var jobs = new List<ConversionJob>();
                var resolver = new OutputPathResolver(output);
                for (var i = 0; i < ranges.Count; i++)
                {
                    var part = JsonFile.Clone(trim);
                    part.Segments = [];
                    part.TrimEnabled = true;
                    part.Start = ranges[i].Start;
                    part.End = ranges[i].Start + (ranges[i].Duration ?? 0);
                    jobs.Add(TrimJob(source, kind, info, part, video, audio, output, resolver, $"_part{i + 1}", null));
                }

                return jobs;
            }

            return [TrimJob(source, kind, info, trim, video, audio, output, new OutputPathResolver(output), null, ranges)];
        }

        return [TrimJob(source, kind, info, trim, video, audio, output, new OutputPathResolver(output), null, null)];
    }

    /// <summary>
    /// One trim/loop job. <paramref name="ranges"/> (multi-cut): the parts are cut one by one with the same settings
    /// and joined without re-encoding. GIF/WebP output turns the result into an animation.
    /// </summary>
    private ConversionJob TrimJob(SourceRef source, MediaKind kind, MediaInfo info, TrimOptions trim, VideoOptions video, AudioOptions audio,
        OutputOptions output, OutputPathResolver resolver, string? suffixOverride, List<ClipRange>? ranges)
    {
        var sourceExt = MediaTypes.Extension(source.Path);
        var clip = ranges is null && trim.TrimEnabled ? ClipRange.From(trim.Start, trim.End) : null;
        var boomerang = trim.Loop == LoopMode.Boomerang;
        var hasFades = trim.FadeIn > 0 || trim.FadeOut > 0;
        var animation = trim.Output is TrimOutput.Gif or TrimOutput.AnimatedWebP;
        var copy = !animation && trim.Mode == TrimMode.Fast && trim.Output == TrimOutput.SameFormat && !hasFades && !boomerang;

        // Target extension and encode settings of the cut parts.
        string ext;
        VideoOptions? encodeVideo = null;
        AudioOptions? encodeAudio = null;
        if (animation)
        {
            ext = trim.Output == TrimOutput.Gif ? "gif" : "webp";
            encodeVideo = FfmpegCommands.SameFormatOptions(info);
        }
        else if (kind == MediaKind.Video)
        {
            if (copy)
            {
                ext = sourceExt;
            }
            else
            {
                encodeVideo = trim.Output == TrimOutput.Convert ? video : FfmpegCommands.SameFormatOptions(info);
                ext = FormatCatalog.Extension(encodeVideo.Container);
            }
        }
        else if (copy)
        {
            ext = sourceExt;
        }
        else
        {
            encodeAudio = trim.Output == TrimOutput.Convert ? audio : SameAudioFormat(sourceExt);
            ext = FormatCatalog.Extension(encodeAudio.Format);
        }

        var loops = !animation && trim.Loop is LoopMode.Count or LoopMode.Duration;
        var suffix = suffixOverride
            ?? (ranges is not null ? "_cut" : clip is not null ? "_trim" : "") + (loops || boomerang ? "_loop" : "");
        if (suffix.Length == 0)
        {
            suffix = animation ? "_anim" : "_edit";
        }

        var plan = resolver.Plan(source.Path, source.Root, ext, suffix);
        var operation = animation ? "OpAnimation" : ranges is not null ? "OpMultiCut" : loops || boomerang ? "OpLoop" : "OpTrim";
        return MediaJob(source, plan, output, kind, operation, Guid.NewGuid(), async (ctx, mi, temp) =>
        {
            // ---------------------------------------------------------- GIF / animated WebP
            if (animation)
            {
                var input = source.Path;
                var animInfo = mi;
                var range = clip;
                var from = 0.0;
                if (ranges is not null)
                {
                    // Join the kept parts first (exact cuts), then make the animation from the joined clip.
                    var joined = ctx.AddTemp(OutputPathResolver.TempPathFor(Path.ChangeExtension(plan.Path, ".mkv"), "join"));
                    var mkv = JsonFile.Clone(encodeVideo!);
                    mkv.Container = VideoContainer.Mkv;
                    await JoinPartsAsync(ctx, mi, source.Path, plan.Path, ranges, kind, copy: false, mkv, null, 0, 0, false, joined, 0, 0.6);
                    input = joined;
                    animInfo = await probe.GetAsync(joined);
                    range = null;
                    from = 0.6;
                }

                var gif = new GifOptions
                {
                    Format = trim.Output == TrimOutput.Gif ? AnimationFormat.Gif : AnimationFormat.WebP,
                    Width = Math.Max(0, trim.AnimationWidth),
                    Fps = Math.Clamp(trim.AnimationFps, 1, 60),
                    Boomerang = boomerang,
                    Start = range?.Start ?? 0,
                    Duration = range?.Duration ?? 0
                };
                await RunAsync(FfmpegCommands.Animation(animInfo, input, temp, gif), ctx, from, 1);
                return;
            }

            // ---------------------------------------------------------- Trim / multi-cut, then loop
            var segmentEnd = loops ? 0.85 : 1;
            var needsSegment = ranges is not null || clip is not null || !copy || hasFades || boomerang || !loops;
            string segment;
            double segmentSeconds;
            if (ranges is not null)
            {
                segment = loops || boomerang ? ctx.AddTemp(OutputPathResolver.TempPathFor(plan.Path, "seg")) : temp;
                segmentSeconds = await JoinPartsAsync(ctx, mi, source.Path, plan.Path, ranges, kind, copy, encodeVideo, encodeAudio,
                    trim.FadeIn, trim.FadeOut, false, segment, 0, boomerang ? segmentEnd * 0.6 : segmentEnd);
                if (boomerang)
                {
                    // Boomerang over the joined result (plays forward, then backward).
                    var joinedInfo = await probe.GetAsync(segment);
                    var target = loops ? ctx.AddTemp(OutputPathResolver.TempPathFor(plan.Path, "boom")) : temp;
                    if (kind == MediaKind.Video)
                    {
                        await RunVideoAsync(env => FfmpegCommands.Video(joinedInfo, segment, target, encodeVideo!, null, 0, 0, true, env),
                            ctx, null, segmentEnd * 0.6, segmentEnd);
                    }
                    else
                    {
                        await RunAsync(FfmpegCommands.Audio(joinedInfo, segment, target, encodeAudio!, null, 0, 0, boomerang: true), ctx, segmentEnd * 0.6, segmentEnd);
                    }

                    segment = target;
                    segmentSeconds *= 2;
                }
            }
            else if (needsSegment)
            {
                segment = loops ? ctx.AddTemp(OutputPathResolver.TempPathFor(plan.Path, "seg")) : temp;
                if (kind == MediaKind.Video && !copy)
                {
                    FfmpegCommand? encoded = null;
                    var target = segment;
                    await RunVideoAsync(env => encoded = FfmpegCommands.Video(mi, source.Path, target, encodeVideo!, clip, trim.FadeIn, trim.FadeOut, boomerang, env),
                        ctx, null, 0, segmentEnd);
                    segmentSeconds = encoded!.ExpectedSeconds;
                }
                else
                {
                    var command = kind == MediaKind.Video
                        ? FfmpegCommands.VideoCopy(mi, source.Path, segment, clip)
                        : copy
                            ? FfmpegCommands.AudioCopy(mi, source.Path, segment, null, clip)
                            : FfmpegCommands.Audio(mi, source.Path, segment, encodeAudio!, clip, trim.FadeIn, trim.FadeOut, boomerang: boomerang);
                    await RunAsync(command, ctx, 0, segmentEnd);
                    segmentSeconds = command.ExpectedSeconds;
                }
            }
            else
            {
                segment = source.Path;
                segmentSeconds = mi.DurationSeconds;
            }

            if (loops && !ReferenceEquals(segment, temp))
            {
                var target = trim.Loop == LoopMode.Duration ? trim.LoopTargetSeconds : (double?)null;
                await RunAsync(FfmpegCommands.Loop(segment, temp, segmentSeconds, trim.LoopCount, target), ctx, segmentEnd, 1);
            }
        });
    }

    /// <summary>
    /// Multi-cut: cuts every range into its own part file (same settings for all, so they can be joined without
    /// re-encoding), then joins them with the concat demuxer. Fade in applies to the first part, fade out to the
    /// last. Returns the length of the result in seconds.
    /// </summary>
    private async Task<double> JoinPartsAsync(JobContext ctx, MediaInfo mi, string input, string planPath, List<ClipRange> ranges, MediaKind kind,
        bool copy, VideoOptions? encodeVideo, AudioOptions? encodeAudio, double fadeIn, double fadeOut, bool boomerang, string joined,
        double from, double to)
    {
        var parts = new List<string>();
        var total = 0.0;
        var step = (to - from) * 0.9 / ranges.Count;
        var partExt = Path.GetExtension(joined);
        for (var i = 0; i < ranges.Count; i++)
        {
            var range = ranges[i];
            var part = ctx.AddTemp(OutputPathResolver.TempPathFor(Path.ChangeExtension(planPath, partExt), $"part{i + 1}"));
            var fIn = i == 0 ? fadeIn : 0;
            var fOut = i == ranges.Count - 1 ? fadeOut : 0;
            var p0 = from + step * i;
            var p1 = p0 + step;
            if (kind == MediaKind.Video && !copy)
            {
                await RunVideoAsync(env => FfmpegCommands.Video(mi, input, part, encodeVideo!, range, fIn, fOut, boomerang, env), ctx, null, p0, p1);
            }
            else
            {
                var command = kind == MediaKind.Video
                    ? FfmpegCommands.VideoCopy(mi, input, part, range)
                    : copy
                        ? FfmpegCommands.AudioCopy(mi, input, part, null, range)
                        : FfmpegCommands.Audio(mi, input, part, encodeAudio!, range, fIn, fOut, boomerang: boomerang);
                await RunAsync(command, ctx, p0, p1);
            }

            parts.Add(part);
            total += range.Duration ?? 0;
        }

        var list = ctx.AddTemp(OutputPathResolver.TempPathFor(Path.ChangeExtension(planPath, ".txt"), "list"));
        await File.WriteAllTextAsync(list, FfmpegCommands.ConcatList(parts));
        await RunAsync(FfmpegCommands.Concat(list, joined, total), ctx, from + (to - from) * 0.9, to);
        return total;
    }

    /// <summary>Re-encode an audio file in its own format at high quality (exact trim, fades).</summary>
    private static AudioOptions SameAudioFormat(string sourceExt)
    {
        var format = FormatCatalog.AudioFormatFromExtension(sourceExt) ?? AudioFormat.Mp3;
        return new AudioOptions
        {
            Format = format,
            BitrateKbps = format == AudioFormat.Opus ? 192 : 320,
            Mp3Mode = BitrateMode.Cbr,
            VorbisQuality = 8,
            FlacLevel = 5,
            WavDepth = WavDepth.Pcm16,
            KeepMetadata = true,
            KeepCover = true,
            NotAboveSource = true
        };
    }

    // ================================================================ Images

    public List<ConversionJob> ImageConvert(IReadOnlyList<SourceRef> sources, ImageOptions imageOptions, OutputOptions outputOptions,
        bool skipSame, string operationKey)
    {
        var image = JsonFile.Clone(imageOptions);
        var output = JsonFile.Clone(outputOptions);
        var resolver = new OutputPathResolver(output);
        var batch = Guid.NewGuid();
        return sources.Select(s =>
        {
            var (format, ext) = ImageEngine.ResolveTarget(image.Format, s.Path);
            var plan = resolver.Plan(s.Path, s.Root, ext);
            var same = skipSame && image.Resize == ResizeMode.Off && FormatCatalog.SameImageType(MediaTypes.Extension(s.Path), format);
            return ImageJob(s, plan, output, operationKey, batch, (ctx, temp) =>
            {
                if (same)
                {
                    throw new JobSkippedException("SkipSameFormat");
                }

                if (!ImageEngine.CanWrite(format))
                {
                    throw new ConversionException("ErrFormatNotWritable", format.ToString());
                }

                ImageEngine.Convert(s.Path, temp, image, format, ctx.CancellationToken);
                if (image.NormalMap)
                {
                    // Game textures: normal map next to the texture ("name_n.png").
                    var normal = Path.Combine(Path.GetDirectoryName(plan.Path) ?? "", Path.GetFileNameWithoutExtension(plan.Path) + "_n.png");
                    GameDevEngine.NormalMap(temp, normal, image.NormalStrength, image.NormalInvertY);
                }
            });
        }).ToList();
    }

    public List<ConversionJob> Icon(IReadOnlyList<SourceRef> sources, IconOptions iconOptions, OutputOptions outputOptions)
    {
        var icon = JsonFile.Clone(iconOptions);
        var output = JsonFile.Clone(outputOptions);
        var resolver = new OutputPathResolver(output);
        var batch = Guid.NewGuid();
        var ext = icon.Export switch
        {
            IconExport.PngSet => "png",
            IconExport.Icns => "icns",
            IconExport.Svg => "svg",
            _ => "ico"
        };
        return sources.Select(s =>
        {
            var plan = resolver.Plan(s.Path, s.Root, ext, icon.Export == IconExport.Ico ? "" : "_icon");
            return ImageJob(s, plan, output, "OpIcon", batch, (ctx, temp) =>
            {
                var package = icon.FaviconPackage
                    ? Path.Combine(Path.GetDirectoryName(plan.Path) ?? "", Path.GetFileNameWithoutExtension(plan.Path) + "_favicon")
                    : null;
                switch (icon.Export)
                {
                    case IconExport.Svg:
                        // A raster image cannot become a real vector graphic: SVG only from SVG sources.
                        if (!MediaTypes.Extension(s.Path).Equals("svg", StringComparison.OrdinalIgnoreCase))
                        {
                            throw new ConversionException("ErrSvgNeedsVector");
                        }

                        File.Copy(s.Path, temp, overwrite: true);
                        break;
                    case IconExport.Icns:
                        GameDevEngine.WriteIcns(s.Path, temp, [16, 32, 64, 128, 256, 512, 1024]);
                        break;
                    case IconExport.PngSet:
                        var sizes = icon.SelectedSizes();
                        GameDevEngine.WritePngSet(s.Path, temp, sizes.Length > 0 ? sizes : [256, 128, 64, 48, 32, 16], plan.Path);
                        break;
                    default:
                        ImageEngine.CreateIcon(s.Path, temp, package, icon, ctx.CancellationToken);
                        return;
                }

                if (package is not null)
                {
                    // Favicon package also with the other formats (the helper .ico is only needed for the package).
                    var helper = Path.Combine(Path.GetTempPath(), $"cvt-{Guid.NewGuid():N}.ico");
                    try
                    {
                        ImageEngine.CreateIcon(s.Path, helper, package, icon, ctx.CancellationToken);
                    }
                    finally
                    {
                        File.Delete(helper);
                    }
                }
            });
        }).ToList();
    }

    // ================================================================ Compress

    /// <summary>Videos (target size with two-pass x264, or CRF), audio files and images; results that are not smaller can be dropped.</summary>
    public List<ConversionJob> Compress(IReadOnlyList<SourceRef> sources, CompressOptions compressOptions, OutputOptions outputOptions)
    {
        var o = JsonFile.Clone(compressOptions);
        var output = JsonFile.Clone(outputOptions);
        var resolver = new OutputPathResolver(output);
        var batch = Guid.NewGuid();
        var jobs = new List<ConversionJob>();
        foreach (var s in sources)
        {
            switch (MediaTypes.Detect(s.Path))
            {
                case MediaKind.Video:
                {
                    var plan = resolver.Plan(s.Path, s.Root, "mp4");
                    jobs.Add(MediaJob(s, plan, output, MediaKind.Video, "OpCompress", batch, async (ctx, info, temp) =>
                    {
                        var warnings = new List<string>();
                        var video = CompressPlanner.Video(info, o, warnings);
                        foreach (var warning in warnings)
                        {
                            ctx.Warn(warning);
                        }

                        // Two passes hit the target size closely; the statistics files are removed with the job.
                        await RunTargetSizeAsync(env => FfmpegCommands.Video(info, s.Path, temp, video, environment: env), video, plan, ctx);

                        EnsureSmaller(o, temp, info.FileSize);
                    }));
                    break;
                }
                case MediaKind.Audio:
                {
                    var format = o.AudioFormat is AudioFormat.Mp3 or AudioFormat.Aac or AudioFormat.Opus ? o.AudioFormat : AudioFormat.Mp3;
                    var plan = resolver.Plan(s.Path, s.Root, FormatCatalog.Extension(format));
                    jobs.Add(MediaJob(s, plan, output, MediaKind.Audio, "OpCompress", batch, async (ctx, info, temp) =>
                    {
                        await RunAsync(FfmpegCommands.Audio(info, s.Path, temp, CompressPlanner.Audio(info, o)), ctx);
                        EnsureSmaller(o, temp, info.FileSize);
                    }));
                    break;
                }
                case MediaKind.Image:
                {
                    var image = CompressPlanner.Image(o);
                    var (format, ext) = ImageEngine.ResolveTarget(image.Format, s.Path);
                    var plan = resolver.Plan(s.Path, s.Root, ext);
                    jobs.Add(ImageJob(s, plan, output, "OpCompress", batch, (ctx, temp) =>
                    {
                        if (!ImageEngine.CanWrite(format))
                        {
                            throw new ConversionException("ErrFormatNotWritable", format.ToString());
                        }

                        ImageEngine.Convert(s.Path, temp, image, format, ctx.CancellationToken);
                        EnsureSmaller(o, temp, new FileInfo(s.Path).Length);
                    }));
                    break;
                }
            }
        }

        return jobs;
    }

    private static void EnsureSmaller(CompressOptions o, string temp, long sourceBytes)
    {
        if (o.OnlyIfSmaller && sourceBytes > 0 && File.Exists(temp) && new FileInfo(temp).Length >= sourceBytes)
        {
            throw new JobSkippedException("SkipNotSmaller");
        }
    }

    // ================================================================ Job helpers

    private ConversionJob MediaJob(SourceRef source, OutputPlan plan, OutputOptions output, MediaKind kind, string operation, Guid batch,
        Func<JobContext, MediaInfo, string, Task> run) =>
        new(source.Path, plan.Path, kind, JobLane.Media, operation, async ctx =>
        {
            if (plan.Skip)
            {
                throw new JobSkippedException("SkipExists");
            }

            if (runner.Locator.FfmpegPath is null)
            {
                throw new ConversionException("ErrFfmpegMissing");
            }

            var info = await probe.GetAsync(source.Path, ctx.CancellationToken).ConfigureAwait(false);
            Directory.CreateDirectory(Path.GetDirectoryName(plan.Path)!);
            var temp = ctx.AddTemp(OutputPathResolver.TempPathFor(plan.Path));
            await run(ctx, info, temp).ConfigureAwait(false);
            Finish(temp, plan.Path, source.Path, output.PreserveDates);
        })
        {
            BatchId = batch
        };

    private static ConversionJob ImageJob(SourceRef source, OutputPlan plan, OutputOptions output, string operation, Guid batch,
        Action<JobContext, string> run) =>
        new(source.Path, plan.Path, MediaKind.Image, JobLane.Image, operation, async ctx =>
        {
            if (plan.Skip)
            {
                throw new JobSkippedException("SkipExists");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(plan.Path)!);
            var temp = ctx.AddTemp(OutputPathResolver.TempPathFor(plan.Path));
            ctx.Report(0.1, null, force: true);
            await Task.Run(() => run(ctx, temp), ctx.CancellationToken).ConfigureAwait(false);
            Finish(temp, plan.Path, source.Path, output.PreserveDates);
            ctx.Report(1, null, force: true);
        })
        {
            BatchId = batch
        };

    /// <summary>
    /// Job whose result is a folder (app icon sets): written into a temporary folder, renamed when complete.
    /// An existing folder of the same name gets a number ("name_appicons (2)").
    /// </summary>
    private static ConversionJob FolderJob(SourceRef source, string folder, string operation, Guid batch, Action<JobContext, string> run) =>
        new(source.Path, folder, MediaKind.Image, JobLane.Image, operation, async ctx =>
        {
            var parent = Path.GetDirectoryName(folder)!;
            Directory.CreateDirectory(parent);
            var temp = Path.Combine(parent, $"{Path.GetFileName(folder)}.cvt-{Guid.NewGuid().ToString("N")[..6]}");
            Directory.CreateDirectory(temp);
            try
            {
                ctx.Report(0.1, null, force: true);
                await Task.Run(() => run(ctx, temp), ctx.CancellationToken).ConfigureAwait(false);
                var final = folder;
                for (var n = 2; Directory.Exists(final) || File.Exists(final); n++)
                {
                    final = $"{folder} ({n})";
                }

                Directory.Move(temp, final);
                ctx.Report(1, null, force: true);
            }
            catch
            {
                try
                {
                    Directory.Delete(temp, recursive: true);
                }
                catch (IOException)
                {
                    // Left for the user; named .cvt-….
                }

                throw;
            }
        })
        {
            BatchId = batch
        };

    /// <summary>App icon sets: one folder per source image ("name_appicons").</summary>
    public List<ConversionJob> AppIcons(IReadOnlyList<SourceRef> sources, AppIconSettings settings, OutputOptions outputOptions)
    {
        var output = JsonFile.Clone(outputOptions);
        var resolver = new OutputPathResolver(output);
        var batch = Guid.NewGuid();
        return sources.Select(s =>
        {
            var plan = resolver.Plan(s.Path, s.Root, "png", "_appicons");
            var folder = Path.Combine(Path.GetDirectoryName(plan.Path) ?? "", Path.GetFileNameWithoutExtension(plan.Path));
            return FolderJob(s, folder, "OpAppIcons", batch, (ctx, temp) => GameDevEngine.WriteAppIcons(s.Path, temp, settings, ctx.CancellationToken));
        }).ToList();
    }

    /// <summary>
    /// Sprite sheet from all sources (one sheet): images become frames of one animation, GIF/WebP files and videos
    /// one animation each (videos are split into frames by FFmpeg first).
    /// </summary>
    public ConversionJob SpriteSheet(IReadOnlyList<SourceRef> sources, string sheetName, string defaultAnimation, SpriteSettings settings,
        int videoFps, OutputOptions outputOptions)
    {
        var output = JsonFile.Clone(outputOptions);
        var resolver = new OutputPathResolver(output);
        var first = sources[0];
        var plan = resolver.Plan(Path.Combine(Path.GetDirectoryName(first.Path) ?? "", sheetName + ".png"), first.Root, "png", "_sheet");
        return ImageJob(first, plan, output, "OpSpriteSheet", Guid.NewGuid(), (ctx, temp) =>
        {
            var groups = new List<SpriteSource>();
            var stills = new List<string>();
            foreach (var source in sources)
            {
                if (MediaTypes.Detect(source.Path) == MediaKind.Video)
                {
                    var folder = Path.Combine(Path.GetTempPath(), $"cvt-frames-{Guid.NewGuid():N}");
                    Directory.CreateDirectory(folder);
                    ctx.AddTemp(folder);
                    runner.RunAsync(FfmpegCommands.Frames(source.Path, Path.Combine(folder, "f%05d.png"), videoFps), ctx, 0.1, 0.5)
                        .GetAwaiter().GetResult();
                    groups.Add(new SpriteSource(Path.GetFileNameWithoutExtension(source.Path), Directory.GetFiles(folder, "*.png").OrderBy(f => f).ToList()));
                }
                else if (MediaTypes.Extension(source.Path) is "gif" or "webp" or "apng")
                {
                    groups.Add(new SpriteSource(Path.GetFileNameWithoutExtension(source.Path), [source.Path]));
                }
                else
                {
                    stills.Add(source.Path);
                }
            }

            if (stills.Count > 0)
            {
                groups.Insert(0, new SpriteSource(string.IsNullOrWhiteSpace(defaultAnimation) ? "default" : defaultAnimation,
                    stills.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList()));
            }

            GameDevEngine.WriteSpriteSheet(groups, temp, plan.Path, settings, ctx.CancellationToken);
        });
    }

    /// <summary>Godot video: Ogg Theora + Vorbis (the only video format Godot's VideoStreamPlayer plays).</summary>
    public List<ConversionJob> GodotVideo(IReadOnlyList<SourceRef> sources, GodotVideoOptions options, OutputOptions outputOptions)
    {
        var o = JsonFile.Clone(options);
        var output = JsonFile.Clone(outputOptions);
        var resolver = new OutputPathResolver(output);
        var batch = Guid.NewGuid();
        return sources.Select(s => MediaJob(s, resolver.Plan(s.Path, s.Root, "ogv"), output, MediaKind.Video, "OpGodotVideo", batch,
            (ctx, info, temp) => RunAsync(FfmpegCommands.GodotVideo(info, s.Path, temp, o), ctx))).ToList();
    }

    // ================================================================ Tools

    private string ModuleExe(ModuleId id) =>
        Modules?.ExecutableOf(id) ?? throw new ConversionException("ErrModuleMissing", ModuleCatalog.Get(id).Name);

    /// <summary>Temporary working folder for frames (deleted with the job's temp files).</summary>
    private static string FrameFolder(JobContext ctx, string name)
    {
        var folder = Path.Combine(Path.GetTempPath(), $"cvt-{name}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        ctx.AddTemp(folder);
        return folder;
    }

    private static int CountPng(string folder)
    {
        try
        {
            return Directory.EnumerateFiles(folder, "*.png").Count();
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// AI upscaling: images directly, videos frame by frame (FFmpeg extracts PNG frames, the upscaler processes the
    /// folder, FFmpeg joins the frames again with the source audio as H.264).
    /// </summary>
    public List<ConversionJob> Upscale(IReadOnlyList<SourceRef> sources, UpscaleOptions upscaleOptions, OutputOptions outputOptions)
    {
        var o = JsonFile.Clone(upscaleOptions);
        var output = JsonFile.Clone(outputOptions);
        var resolver = new OutputPathResolver(output);
        var batch = Guid.NewGuid();
        var suffix = $"_x{Math.Clamp(o.Scale, 2, 4)}";
        var jobs = new List<ConversionJob>();
        foreach (var s in sources)
        {
            if (MediaTypes.Detect(s.Path) == MediaKind.Video)
            {
                var plan = resolver.Plan(s.Path, s.Root, "mp4", suffix);
                jobs.Add(MediaJob(s, plan, output, MediaKind.Video, "OpUpscale", batch, async (ctx, info, temp) =>
                {
                    var video = info.MainVideo ?? throw new ConversionException("ErrNoVideoStream");
                    var input = FrameFolder(ctx, "in");
                    var result = FrameFolder(ctx, "up");
                    await RunAsync(FfmpegCommands.AllFrames(info, s.Path, Path.Combine(input, "%08d.png")), ctx, 0, 0.1).ConfigureAwait(false);
                    var total = Math.Max(1, CountPng(input));
                    var (module, args, native) = ModuleArgs.Upscale(o, input, result, folder: true);
                    await ModuleRunner.RunAsync(ModuleExe(module), args, ctx, 0.1, 0.85, () => CountPng(result) / (double)total).ConfigureAwait(false);
                    var resize = native == o.Scale ? 1 : o.Scale / (double)native;
                    await RunAsync(FfmpegCommands.FromFrames(Path.Combine(result, "%08d.png"), video.Fps, s.Path, info, temp, o.VideoCrf,
                        info.DurationSeconds, resize), ctx, 0.85, 1).ConfigureAwait(false);
                }));
            }
            else
            {
                var format = o.ImageFormat is "jpg" or "webp" ? o.ImageFormat : "png";
                var plan = resolver.Plan(s.Path, s.Root, format, suffix);
                jobs.Add(new ConversionJob(s.Path, plan.Path, MediaKind.Image, JobLane.Media, "OpUpscale", async ctx =>
                {
                    if (plan.Skip)
                    {
                        throw new JobSkippedException("SkipExists");
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(plan.Path)!);
                    var temp = ctx.AddTemp(OutputPathResolver.TempPathFor(plan.Path));
                    var work = FrameFolder(ctx, "img");
                    var input = s.Path;
                    if (!UpscaleImages.ToolCanRead(input))
                    {
                        input = Path.Combine(work, "source.png");
                        UpscaleImages.ToPng(s.Path, input);
                    }

                    var (width, height) = UpscaleImages.Size(input);
                    var raw = Path.Combine(work, "upscaled.png");
                    var (module, args, _) = ModuleArgs.Upscale(o, input, raw, folder: false);
                    await ModuleRunner.RunAsync(ModuleExe(module), args, ctx, 0.05, 0.9).ConfigureAwait(false);
                    await Task.Run(() => UpscaleImages.Finish(raw, temp, width, height, o.Scale, format, 92), ctx.CancellationToken).ConfigureAwait(false);
                    Finish(temp, plan.Path, s.Path, output.PreserveDates);
                })
                {
                    BatchId = batch
                });
            }
        }

        return jobs;
    }

    /// <summary>Denoise with FFmpeg filters (video: hqdn3d/nlmeans, audio: afftdn/anlmdn).</summary>
    public List<ConversionJob> Denoise(IReadOnlyList<SourceRef> sources, DenoiseOptions denoiseOptions, OutputOptions outputOptions)
    {
        var o = JsonFile.Clone(denoiseOptions);
        var output = JsonFile.Clone(outputOptions);
        var resolver = new OutputPathResolver(output);
        var batch = Guid.NewGuid();
        return sources.Select(s =>
        {
            var kind = MediaTypes.Detect(s.Path);
            var ext = MediaTypes.Extension(s.Path);
            var target = kind == MediaKind.Video
                ? ext is "mp4" or "mkv" or "mov" or "m4v" ? ext : "mp4"
                : ext is "flac" or "wav" or "mp3" or "ogg" or "opus" or "m4a" ? ext : "m4a";
            return MediaJob(s, resolver.Plan(s.Path, s.Root, target, "_denoised"), output, kind, "OpDenoise", batch,
                (ctx, info, temp) => RunAsync(FfmpegCommands.Denoise(info, s.Path, temp, o), ctx));
        }).ToList();
    }

    /// <summary>
    /// Frame interpolation: RIFE (module, frames → folder → video) or FFmpeg minterpolate. Slow motion keeps the
    /// original frame rate, so the clip plays Factor× slower (without audio).
    /// </summary>
    public List<ConversionJob> Interpolate(IReadOnlyList<SourceRef> sources, InterpolateOptions interpolateOptions, OutputOptions outputOptions)
    {
        var o = JsonFile.Clone(interpolateOptions);
        var output = JsonFile.Clone(outputOptions);
        var resolver = new OutputPathResolver(output);
        var batch = Guid.NewGuid();
        var factor = Math.Clamp(o.Factor, 2, 4);
        var suffix = o.SlowMotion ? $"_slow{factor}x" : $"_{factor}xfps";
        return sources.Select(s => MediaJob(s, resolver.Plan(s.Path, s.Root, "mp4", suffix), output, MediaKind.Video, "OpInterpolate", batch,
            async (ctx, info, temp) =>
            {
                if (o.Engine == InterpolationEngine.Ffmpeg)
                {
                    await RunAsync(FfmpegCommands.Minterpolate(info, s.Path, temp, o), ctx).ConfigureAwait(false);
                    return;
                }

                var video = info.MainVideo ?? throw new ConversionException("ErrNoVideoStream");
                var input = FrameFolder(ctx, "in");
                var result = FrameFolder(ctx, "rife");
                await RunAsync(FfmpegCommands.AllFrames(info, s.Path, Path.Combine(input, "%08d.png")), ctx, 0, 0.1).ConfigureAwait(false);
                var count = Math.Max(1, CountPng(input));
                var targetCount = count * factor;
                await ModuleRunner.RunAsync(ModuleExe(ModuleId.Rife), ModuleArgs.Rife(input, result, targetCount), ctx, 0.1, 0.85,
                    () => CountPng(result) / (double)targetCount).ConfigureAwait(false);
                var fps = (video.Fps > 0 ? video.Fps : 30) * (o.SlowMotion ? 1 : factor);
                await RunAsync(FfmpegCommands.FromFrames(Path.Combine(result, "%08d.png"), fps, o.SlowMotion ? null : s.Path, info, temp, o.VideoCrf,
                    info.DurationSeconds * (o.SlowMotion ? factor : 1)), ctx, 0.85, 1).ConfigureAwait(false);
            })).ToList();
    }

    private Task RunAsync(FfmpegCommand command, JobContext ctx, double from = 0, double to = 1)
    {
        foreach (var warning in command.Warnings)
        {
            ctx.Warn(warning);
        }

        return runner.RunAsync(command, ctx, from, to);
    }

    /// <summary>Moves the finished temp file to its final name and copies the source dates.</summary>
    private static void Finish(string temp, string final, string source, bool preserveDates)
    {
        if (!File.Exists(temp))
        {
            throw new ConversionException("ErrNoOutput");
        }

        if (File.Exists(final))
        {
            File.Delete(final);
        }

        File.Move(temp, final);
        if (preserveDates)
        {
            try
            {
                File.SetCreationTime(final, File.GetCreationTime(source));
                File.SetLastWriteTime(final, File.GetLastWriteTime(source));
            }
            catch
            {
                // Dates are a nicety.
            }
        }
    }
}
