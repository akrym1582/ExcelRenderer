# Third-party notices

ExcelRenderer's MIT license does not cover the third-party fonts listed below.

## Noto Sans CJK JP

- File: `third_party/NotoSansJP/NotoSansCJKjp-Regular.otf`
- Copyright: Copyright 2014-2021 Adobe (http://www.adobe.com/), with Reserved
  Font Name "Source"
- License: SIL Open Font License, Version 1.1
- Upstream: <https://github.com/notofonts/noto-cjk>
- License text: [`third_party/NotoSansJP/OFL.txt`](third_party/NotoSansJP/OFL.txt)

The regular font is embedded in the optional ExcelRenderer.Fonts package, which
ExcelRenderer.Tool installs as a dependency. Its license text is in the font package.

## Noto Serif CJK JP

- File: `third_party/NotoSerifCJKJP/NotoSerifCJKjp-Regular.otf` (unmodified)
- License: SIL Open Font License, Version 1.1
- Upstream: <https://github.com/notofonts/noto-cjk/blob/f8d157532fbfaeda587e826d4cd5b21a49186f7c/Serif/OTF/Japanese/NotoSerifCJKjp-Regular.otf>
- License text: [`third_party/NotoSerifCJKJP/LICENSE`](third_party/NotoSerifCJKJP/LICENSE)
- SHA-256: `d9854c7a8ef170b5a7932558856fd64eb8de0b007cd823fed6f9f514ad2803d3`

The regular font is embedded in ExcelRenderer.Fonts, a dependency of
ExcelRenderer.Tool. Its license text is in the font package.

## IPAmj Mincho

- File: `third_party/IPAmjMincho/ipamjm.ttf` (version 006.01, unmodified)
- License: IPA Font License Agreement v1.0
- Upstream: <https://moji.or.jp/mojikiban/font/>
- License text: [`third_party/IPAmjMincho/IPA_Font_License_Agreement_v1.0.txt`](third_party/IPAmjMincho/IPA_Font_License_Agreement_v1.0.txt)
- Original readme: [`third_party/IPAmjMincho/Readme.txt`](third_party/IPAmjMincho/Readme.txt)
- SHA-256: `a3e84f495f3c388db7a1473bf1985c1c076d0c814100f10a027ca6853eb1e8cb`

The original font is embedded in ExcelRenderer.Fonts, a dependency of
ExcelRenderer.Tool. The IPA Font License Agreement and upstream readme are
included in the font package. Use and redistribution are subject to the IPA
Font License Agreement v1.0. The renderer uses this unmodified font as the
second IVS fallback after the selected Noto Sans CJK JP or Noto Serif CJK JP
style. Unsupported variation sequences are reported rather than silently
dropping the variation selector.
