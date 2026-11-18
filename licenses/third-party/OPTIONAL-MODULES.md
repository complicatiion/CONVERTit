# Optional modules (AI upscaling, frame interpolation)

None of these modules is shipped with CONVERTit. They are downloaded only when you click *Download* on the
*Dependencies* page – always from the fixed (pinned) release of the original project on GitHub, checked against the
SHA-256 digest GitHub publishes for the file (where GitHub provides one; otherwise the hash is recorded and shown as "not verified") – or you place them yourself (*Install from ZIP*, *Choose folder*).
CONVERTit starts them as separate programs and does not link them.

Module folders: `%LocalAppData%\CONVERTit\modules\<module>` (portable: `data\modules\<module>`), for all users
`C:\ProgramData\CONVERTit\modules\<module>`. The archives keep their own license files in the module folder.

| Module | Release | License | Copyright | Source |
| --- | --- | --- | --- | --- |
| Real-ESRGAN (ncnn-vulkan build with models) | `xinntao/Real-ESRGAN` v0.2.5.0 · `realesrgan-ncnn-vulkan-20220424-windows.zip` | BSD-3-Clause | Copyright (c) 2021 Xintao Wang | https://github.com/xinntao/Real-ESRGAN |
| waifu2x-ncnn-vulkan (CUnet models) | `nihui/waifu2x-ncnn-vulkan` 20220728 | MIT | Copyright (c) 2019 nihui | https://github.com/nihui/waifu2x-ncnn-vulkan |
| RIFE ncnn-vulkan (rife-v4.6 model) | `nihui/rife-ncnn-vulkan` 20221029 | MIT | Copyright (c) 2021 nihui; RIFE model: hzwer (Megvii Research), MIT | https://github.com/nihui/rife-ncnn-vulkan · https://github.com/hzwer/ECCV2022-RIFE |
| ncnn (inside all three tools) | – | BSD-3-Clause | Copyright (C) Tencent | https://github.com/Tencent/ncnn |

**Not offered:** Real-CUGAN. The ncnn port is MIT-licensed, but the license of the trained models published by
bilibili is not stated clearly enough for an automatic download. You can still install a build yourself under your
own responsibility (*Install from ZIP* is available for the listed modules only).

Full texts: [BSD 3-Clause](LICENSE-BSD-3-CLAUSE.md) · [MIT](LICENSE-MIT.md)
