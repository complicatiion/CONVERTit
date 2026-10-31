# ImageMagick · Magick.NET

| | |
| --- | --- |
| Package | Magick.NET-Q8-x64 14.x (NuGet) |
| License | Magick.NET: Apache License 2.0 · ImageMagick: ImageMagick License (Apache-2.0 style) |
| Copyright | Magick.NET: Copyright Dirk Lemstra · ImageMagick: Copyright ImageMagick Studio LLC |
| Source | https://github.com/dlemstra/Magick.NET · https://github.com/ImageMagick/ImageMagick |
| License pages | https://github.com/dlemstra/Magick.NET/blob/main/License.txt · https://imagemagick.org/script/license.php |
| Use in CONVERTit | Image conversion (PNG, JPEG, WebP, AVIF, TIFF, BMP, GIF, ICO), resizing, multi-size icons, previews |

## Native libraries inside Magick.NET

The native part of Magick.NET (`Magick.Native-Q8-x64.dll`) contains ImageMagick and its format libraries,
among them libpng, libjpeg-turbo, libwebp, libtiff, libheif, libde265, libaom, libjxl, zlib, freetype, lcms2,
libxml2, OpenJPEG and others. They are licensed under permissive licenses (BSD, MIT, zlib, libpng, IJG) and,
for libheif and libde265, under the GNU LGPL. Magick.NET publishes the full list with all license texts in its
`Notice.txt`; `build\publish.ps1` copies this file from the NuGet package to
`licenses\third-party\MAGICK-NET-NOTICE.txt` of every release.

The LGPL-licensed parts are used unmodified as distributed by the Magick.NET project. Their source code is
available from the respective projects (https://github.com/strukturag/libheif, https://github.com/strukturag/libde265)
and from the Magick.NET repository, which documents how the native library is built.

Full Apache 2.0 text: [LICENSE-APACHE-2.0.md](LICENSE-APACHE-2.0.md) · LGPL 2.1 text: [LICENSE-LGPL-2.1.md](LICENSE-LGPL-2.1.md)
