# Third-party software in Mo Converter

Mo Converter is © Mohamad Mourad and licensed under the MIT License (see `LICENSE`).
It uses the following software from other authors, each under its own license.

## Shipped with the program

| Software | Used for | License |
|---|---|---|
| [FFmpeg](https://ffmpeg.org) 9.0.2 (LGPL build by [FFmpeg-Builds](https://github.com/BtbN/FFmpeg-Builds)) | Video and audio | LGPL-3.0-or-later |
| [Magick.NET](https://github.com/dlemstra/Magick.NET) / ImageMagick | Images | Apache-2.0 / ImageMagick License |
| [PDFsharp](https://github.com/empira/PDFsharp) 6.2.4 | Merging, splitting, rotating, compressing PDFs | MIT |
| [PdfPig](https://github.com/UglyToad/PdfPig) | Reading text and layout from PDFs | Apache-2.0 |
| [Open XML SDK](https://github.com/dotnet/Open-XML-SDK) | Word, Excel and PowerPoint files | MIT |
| [Markdig](https://github.com/xoofx/markdig) | Reading Markdown | BSD-2-Clause |
| [SharpCompress](https://github.com/adamhathcock/sharpcompress) | Reading ZIP, 7z, RAR, TAR and GZ archives | MIT |
| [QRCoder](https://github.com/Shane32/QRCoder) | Making QR codes | MIT |
| [ZXing.Net](https://github.com/micjahn/ZXing.Net) | Reading QR codes and barcodes | Apache-2.0 |
| [H.NotifyIcon](https://github.com/HavenDV/H.NotifyIcon) | Icon next to the clock | MIT |
| [Velopack](https://github.com/velopack/velopack) | Installer and updates | MIT |
| [.NET](https://github.com/dotnet/runtime) and WPF | Runtime and user interface | MIT |

## About FFmpeg

FFmpeg is a separate program. Mo Converter starts it (`ffmpeg\ffmpeg.exe`) and does not contain or change its code.

- The build is the LGPL variant: the GPL parts (x264, x265) are not included.
- FFmpeg's libraries are separate DLL files in the `ffmpeg` folder; you may replace them with your own build.
- The full license text is in `ffmpeg\LICENSE.txt`.
- The source code of this exact version: <https://github.com/FFmpeg/FFmpeg/tree/d838f9f9ae> (n9.0.2-57-gd838f9f9ae).
  Build scripts: <https://github.com/BtbN/FFmpeg-Builds>.

H.264 video is encoded with the encoder that is part of Windows (Media Foundation) where available.

Text recognition (OCR) uses the recognizer built into Windows (Windows.Media.Ocr) with the languages installed there.

## Used when installed, not shipped

[LibreOffice](https://www.libreoffice.org) (MPL-2.0): when it is installed on the computer, Mo Converter starts it to
turn office documents into PDF and between Office and OpenDocument formats. It is not part of Mo Converter.

## Not shipped (used only to build and test)

xUnit (Apache-2.0), Microsoft.NET.Test.Sdk (MIT), vpk (MIT).
