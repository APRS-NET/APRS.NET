# Changelog

## [0.1.0-alpha.9](https://github.com/APRS-NET/APRS.NET/compare/v0.1.0-alpha.8...v0.1.0-alpha.9) (2026-10-13)


### Bug Fixes

* **packets:** read unpadded message addressees with a warning ([c93adf8](https://github.com/APRS-NET/APRS.NET/commit/c93adf8403994de8da29d59c5be5ca1945e3151d))

## [0.1.0-alpha.8](https://github.com/APRS-NET/APRS.NET/compare/v0.1.0-alpha.7...v0.1.0-alpha.8) (2026-10-13)


### Features

* **packets:** decode weather reports ([0df3271](https://github.com/APRS-NET/APRS.NET/commit/0df32710b0e1e44e43dc4edcea6c157ecd849679))

## [0.1.0-alpha.7](https://github.com/APRS-NET/APRS.NET/compare/v0.1.0-alpha.6...v0.1.0-alpha.7) (2026-10-13)


### Features

* **packets:** decode telemetry reports ([acab24d](https://github.com/APRS-NET/APRS.NET/commit/acab24d1285f9cc46b687a91576076d16c1458fd))

## [0.1.0-alpha.6](https://github.com/APRS-NET/APRS.NET/compare/v0.1.0-alpha.5...v0.1.0-alpha.6) (2026-10-13)


### Features

* **packets:** decode Mic-E reports ([69bbc31](https://github.com/APRS-NET/APRS.NET/commit/69bbc31ae43354cb5214bf684faf2faca1928762))

## [0.1.0-alpha.5](https://github.com/APRS-NET/APRS.NET/compare/v0.1.0-alpha.4...v0.1.0-alpha.5) (2026-10-13)


### Features

* **packets:** decode messages and telemetry definitions ([21cd0b4](https://github.com/APRS-NET/APRS.NET/commit/21cd0b40a9fc675684bb41f1ae130fbadc987022))
* **packets:** decode objects and items ([eb75824](https://github.com/APRS-NET/APRS.NET/commit/eb75824995db8b5b961365d64dbb0a41d31f0652))
* **packets:** decode position reports ([de94860](https://github.com/APRS-NET/APRS.NET/commit/de94860b75d2af6225f2a57ea87b83a02b7a8fc9))
* **packets:** decode status reports ([33206c0](https://github.com/APRS-NET/APRS.NET/commit/33206c048ffc81396e6761878b6da81f79fb0736))


### Bug Fixes

* **aprs-is:** count discarded over-long lines as dropped packets ([a68ccf8](https://github.com/APRS-NET/APRS.NET/commit/a68ccf877038915dfa54ab2b604246365842c422))

## [0.1.0-alpha.4](https://github.com/APRS-NET/APRS.NET/compare/v0.1.0-alpha.3...v0.1.0-alpha.4) (2026-10-12)


### Features

* **symbols:** add the AprsNet.Symbols package ([ead2f48](https://github.com/APRS-NET/APRS.NET/commit/ead2f484c989c3176fb8efd0a5a2cfe12c236fbc))

## [0.1.0-alpha.3](https://github.com/APRS-NET/APRS.NET/compare/v0.1.0-alpha.2...v0.1.0-alpha.3) (2026-10-12)


### Features

* **packets:** parse the TNC2 header of APRS packets ([3e84db9](https://github.com/APRS-NET/APRS.NET/commit/3e84db975932cd072196d63b221fae39e5914dec))

## [0.1.0-alpha.2](https://github.com/APRS-NET/APRS.NET/compare/v0.1.0-alpha.1...v0.1.0-alpha.2) (2026-10-12)


### Bug Fixes

* **corpus:** close anonymizer gaps found in a new capture ([e3bc8d0](https://github.com/APRS-NET/APRS.NET/commit/e3bc8d0a094dcecb2c70152c1c7a646347043ad3))

## 0.1.0-alpha.1 (2026-10-12)


### Bug Fixes

* **aprs-is:** detect stalled connections and add --reconnect ([b2e1db6](https://github.com/APRS-NET/APRS.NET/commit/b2e1db6901c633a3f53303cc0b5fe84e7cc93c7b))
* **build:** relax SDK pin to 10.0.100 floor and apply csharpier formatting ([cecfe84](https://github.com/APRS-NET/APRS.NET/commit/cecfe84d67b632aed10208b3cc8c763b0ea16a7e))
* **corpus:** keep captured packets byte-exact on Windows checkouts ([3e26c12](https://github.com/APRS-NET/APRS.NET/commit/3e26c12177e8846fb0cd72012e04e785dcdfdf8c))


### Miscellaneous Chores

* **release:** release alpha pre-releases until 1.0.0 ([0a5ac89](https://github.com/APRS-NET/APRS.NET/commit/0a5ac892b9e6b511de3110c1c5fa8a4d8cd5c525))
* **release:** start releases at 0.1.0 ([32fcaea](https://github.com/APRS-NET/APRS.NET/commit/32fcaeabc507f4a84e22972274cddc3ff83a15cb))
