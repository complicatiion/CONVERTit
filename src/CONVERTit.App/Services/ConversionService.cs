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
                    await RunTargetSizeAsync(FfmpegCommands.Video(info, s.Path, temp, video), CompressPlanner.ResolveTargetSize(info, video), plan, ctx);
                }
                else
                {
                    await RunAsync(FfmpegCommands.Video(info, s.Path, temp, video), ctx);
                }
            });
        }).ToList();
    }

    /// <summary>Bitrate encodes for a size: two passes with x264 (close to the size), one pass otherwise.</summary>
    private async Task RunTargetSizeAsync(FfmpegCommand command, VideoOptions resolved, OutputPlan plan, JobContext ctx)
    {
        if (resolved.QualityMode == VideoQualityMode.Bitrate && resolved.Codec == VideoCodec.H264 && resolved.Acceleration == Acceleration.Off)
        {
            var log = OutputPathResolver.TempPathFor(plan.Path, "pass");
            ctx.AddTemp(log + "-0.log");
            ctx.AddTemp(log + "-0.log.mbtree");
            var (pass1, pass2) = FfmpegCommands.TwoPass(command, log);
            await RunAsync(pass1, ctx, 0, 0.4);
            await RunAsync(pass2, ctx, 0.4, 1);
        }
        else
        {
            await RunAsync(command, ctx);
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
        var resolver = new OutputPathResolver(output);

        var sourceExt = MediaTypes.Extension(source.Path);
        var clip = trim.TrimEnabled ? ClipRange.From(trim.Start, trim.End) : null;
        var boomerang = trim.Loop == LoopMode.Boomerang;
        var hasFades = trim.FadeIn > 0 || trim.FadeOut > 0;
        var copy = trim.Mode == TrimMode.Fast && trim.Output == TrimOutput.SameFormat && !hasFades && !boomerang;

        // Target extension.
        string ext;
        VideoOptions? encodeVideo = null;
        AudioOptions? encodeAudio = null;
        if (kind == MediaKind.Video)
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
        else
        {
            if (copy)
            {
                ext = sourceExt;
            }
            else
            {
                encodeAudio = trim.Output == TrimOutput.Convert ? audio : SameAudioFormat(sourceExt);
                ext = FormatCatalog.Extension(encodeAudio.Format);
            }
        }

        var loops = trim.Loop is LoopMode.Count or LoopMode.Duration;
        var suffix = (clip is not null ? "_trim" : "") + (loops || boomerang ? "_loop" : "");
        if (suffix.Length == 0)
        {
            suffix = "_edit";
        }

        var plan = resolver.Plan(source.Path, source.Root, ext, suffix);
        var job = MediaJob(source, plan, output, kind, loops || boomerang ? "OpLoop" : "OpTrim", Guid.NewGuid(), async (ctx, mi, temp) =>
        {
            var segmentEnd = loops ? 0.85 : 1;
            // Without trim and loop the segment run is the whole job (e.g. fades only, or a plain copy).
            var needsSegment = clip is not null || !copy || hasFades || boomerang || !loops;
            string segment;
            double segmentSeconds;
            if (needsSegment)
            {
                segment = loops ? ctx.AddTemp(OutputPathResolver.TempPathFor(plan.Path, "seg")) : temp;
                var command = kind == MediaKind.Video
                    ? copy
                        ? FfmpegCommands.VideoCopy(mi, source.Path, segment, clip)
                        : FfmpegCommands.Video(mi, source.Path, segment, encodeVideo!, clip, trim.FadeIn, trim.FadeOut, boomerang)
                    : copy
                        ? FfmpegCommands.AudioCopy(mi, source.Path, segment, null, clip)
                        : FfmpegCommands.Audio(mi, source.Path, segment, encodeAudio!, clip, trim.FadeIn, trim.FadeOut, boomerang: boomerang);
                await RunAsync(command, ctx, 0, segmentEnd);
                segmentSeconds = command.ExpectedSeconds;
            }
            else
            {
                segment = source.Path;
                segmentSeconds = mi.DurationSeconds;
            }

            if (loops)
            {
                var target = trim.Loop == LoopMode.Duration ? trim.LoopTargetSeconds : (double?)null;
                await RunAsync(FfmpegCommands.Loop(segment, temp, segmentSeconds, trim.LoopCount, target), ctx, segmentEnd, 1);
            }
        });
        return [job];
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
            });
        }).ToList();
    }

    public List<ConversionJob> Icon(IReadOnlyList<SourceRef> sources, IconOptions iconOptions, OutputOptions outputOptions)
    {
        var icon = JsonFile.Clone(iconOptions);
        var output = JsonFile.Clone(outputOptions);
        var resolver = new OutputPathResolver(output);
        var batch = Guid.NewGuid();
        return sources.Select(s =>
        {
            var plan = resolver.Plan(s.Path, s.Root, "ico");
            return ImageJob(s, plan, output, "OpIcon", batch, (ctx, temp) =>
            {
                var package = icon.FaviconPackage
                    ? Path.Combine(Path.GetDirectoryName(plan.Path) ?? "", Path.GetFileNameWithoutExtension(plan.Path) + "_favicon")
                    : null;
                ImageEngine.CreateIcon(s.Path, temp, package, icon, ctx.CancellationToken);
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
                        await RunTargetSizeAsync(FfmpegCommands.Video(info, s.Path, temp, video), video, plan, ctx);

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
