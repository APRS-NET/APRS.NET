# AprsNet.Symbols

APRS symbol meanings and scalable SVG images of every symbol, in a conventional and a modern style.

> **Alpha pre-release.** APRS.NET packages are versioned `0.x.y-alpha.N` until at least 1.0.0, and the API can change
> between any two releases. Install with `--prerelease`. Not for production use.

```csharp
var symbol = new AprsSymbol('L', '&');                    // from a packet: table (or overlay) and code

AprsSymbolCatalog.GetName(symbol);                        // "Lora Igate" (the APRS 1.2 overlay meaning)
AprsSymbolCatalog.Get(symbol).Category;                   // AprsSymbolCategory.Radio
string svg = AprsSymbolImages.GetSvg(symbol, AprsSymbolStyle.Modern);
```

Every image is drawn by this library on a 48×48 view box, so it scales cleanly to any size. Overlay characters use the
library's own outline font and look the same in every renderer. Unassigned codes get a shared "not" sign.

- **Conventional:** shapes and colours long-time APRS users recognise, with dark outlines.
- **Modern:** white glyphs on discs coloured by category (radio, vehicles, weather and so on).

See every symbol in both styles in the
[conventional](https://github.com/APRS-NET/APRS.NET/blob/main/docs/symbols/conventional.md) and
[modern](https://github.com/APRS-NET/APRS.NET/blob/main/docs/symbols/modern.md) symbol pages.

The symbol meanings come from the specification text (APRS101 Appendix 2 and the APRS 1.1 and 1.2 symbol lists). The
drawings are original work for this library, designed from those descriptions, and are MIT licensed like the code.

Part of [APRS.NET](https://github.com/APRS-NET/APRS.NET).

<sub>Developed with AI assistance; see the [AI declaration](https://github.com/APRS-NET/APRS.NET/blob/main/AI-DECLARATION.md).</sub>
