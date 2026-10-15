# Changelog

## [0.2.0-alpha.2](https://github.com/APRS-NET/APRS.NET/compare/v0.2.0-alpha.1...v0.2.0-alpha.2) (2026-10-15)


### ⚠ BREAKING CHANGES

* **packets:** MicEReport.Device is an AprsDevice (vendor, model, class, operating system, features) instead of a model name string. The CLI prints it as vendor and model, such as "Yaesu FTM-400DR".

### Features

* **deviceid:** move device identification into AprsNet.DeviceId ([f50646d](https://github.com/APRS-NET/APRS.NET/commit/f50646d22ae1b885040bbc4f01dc60461dcbc68d))
* **deviceid:** move device identification into AprsNet.DeviceId ([4b5669c](https://github.com/APRS-NET/APRS.NET/commit/4b5669cf97cadf1b9c1e7bd9c9d1362eb7249e15)), closes [#119](https://github.com/APRS-NET/APRS.NET/issues/119)
* **filters:** build APRS-IS filters from typed terms ([95196f2](https://github.com/APRS-NET/APRS.NET/commit/95196f2e3d2119de3071e17690ebf18ddbbd4dc5))
* **filters:** build APRS-IS filters from typed terms ([77fd742](https://github.com/APRS-NET/APRS.NET/commit/77fd742467c0b8d85959c12933ad4eab2a290f5c)), closes [#117](https://github.com/APRS-NET/APRS.NET/issues/117)
* **filters:** publish AprsNet.Filters ([2eec7b0](https://github.com/APRS-NET/APRS.NET/commit/2eec7b0de0badb5bec4542c42daade256f158b8d))
* **filters:** publish AprsNet.Filters ([5ca6567](https://github.com/APRS-NET/APRS.NET/commit/5ca65678ba7dff3ba6d5b1a37448980ad4e758a8))
* **packets:** add geodesy and unit conversions ([5f29f04](https://github.com/APRS-NET/APRS.NET/commit/5f29f04fbb94b22336e83be8df6dd93053585e60))
* **packets:** add geodesy and unit conversions ([66deffb](https://github.com/APRS-NET/APRS.NET/commit/66deffb169131b5dd79454b7808e99c83927ec2f)), closes [#120](https://github.com/APRS-NET/APRS.NET/issues/120)
* **packets:** analyse how a packet travelled from its path ([55531d1](https://github.com/APRS-NET/APRS.NET/commit/55531d150f8357a32b827433c5382a32f5d3c088))
* **packets:** analyse how a packet travelled from its path ([1650f8a](https://github.com/APRS-NET/APRS.NET/commit/1650f8a2ef9301c7d48a2925ea5d3ceb5b5e5ab5)), closes [#121](https://github.com/APRS-NET/APRS.NET/issues/121)
* **packets:** detect duplicate packets ([3f8e5ca](https://github.com/APRS-NET/APRS.NET/commit/3f8e5ca72b441d7cbc49de14259a1466801c10b2)), closes [#118](https://github.com/APRS-NET/APRS.NET/issues/118)
* **packets:** identify devices from the aprs-deviceid list ([cf127ea](https://github.com/APRS-NET/APRS.NET/commit/cf127ea4f07d17e6883e5ebb66bffd6f723cbdf6)), closes [#119](https://github.com/APRS-NET/APRS.NET/issues/119)
* **packets:** merge packet sources and report duplicates by source ([ec4b956](https://github.com/APRS-NET/APRS.NET/commit/ec4b9561ef5b0a660c2fdd6b23c0152424fe9495))
* **packets:** merge packet sources and report duplicates by source ([5d14e32](https://github.com/APRS-NET/APRS.NET/commit/5d14e325c4d375850568e4eb7fb71bf868761462)), closes [#125](https://github.com/APRS-NET/APRS.NET/issues/125)

## [0.2.0-alpha.1](https://github.com/APRS-NET/APRS.NET/compare/v0.1.0-alpha.17...v0.2.0-alpha.1) (2026-10-15)


### ⚠ BREAKING CHANGES

* **packets:** WeatherReport.Timestamp is now AprsTimestamp? instead of AprsTimestamp.
* the packages no longer support net8.0; apps on .NET 8 stay on 0.1.0-alpha.18 or earlier.

### Features

* **aprs-is:** count messages to the login callsign, active connections and bytes sent ([dbd6dce](https://github.com/APRS-NET/APRS.NET/commit/dbd6dce3e96da687d5846e984327bcccfa64363e))
* **aprs-is:** count messages to the login callsign, active connections and bytes sent ([60545da](https://github.com/APRS-NET/APRS.NET/commit/60545dacc1a7591436c3fe21062e2ed7af7299c0))
* **aprs-is:** count parse failures by reason ([b76a601](https://github.com/APRS-NET/APRS.NET/commit/b76a601bc8bba494711d68ca52babbd16621513f))
* **aprs-is:** count parse failures by reason ([954a310](https://github.com/APRS-NET/APRS.NET/commit/954a310113dec192f914e9537b80b91ae0e4decb))
* **aprs-is:** make every AprsIsClientOptions setting settable ([0f77d1d](https://github.com/APRS-NET/APRS.NET/commit/0f77d1d8507aa69d22a2b34f5370415f2d4fd88a))
* **aprs-is:** read parsed packets with ReadPacketsAsync ([0de0b42](https://github.com/APRS-NET/APRS.NET/commit/0de0b42143354d7e3b4a78c3ee247b153fac4497))
* **aprs-is:** read parsed packets with ReadPacketsAsync ([77ed23d](https://github.com/APRS-NET/APRS.NET/commit/77ed23d7ae389149a2f2490c881a711be7cd3f2a))
* **ax25:** decode and encode AX.25 UI frames ([2d034ec](https://github.com/APRS-NET/APRS.NET/commit/2d034ec23fb634cf5b820ab357ea6d18a9d15ead))
* **ax25:** decode and encode AX.25 UI frames ([f701526](https://github.com/APRS-NET/APRS.NET/commit/f701526ed3468b76604366bd476b7869dc7629d3))
* **di:** register an APRS-IS connection from configuration ([576378b](https://github.com/APRS-NET/APRS.NET/commit/576378b186f8f2c406468789142eb253481eb6c0))
* **di:** register an APRS-IS connection from configuration ([9f7a113](https://github.com/APRS-NET/APRS.NET/commit/9f7a113c319b21cb70f55190e9c791e5f5acfcda))
* **filters:** evaluate APRS-IS filter expressions locally ([fedc8d9](https://github.com/APRS-NET/APRS.NET/commit/fedc8d96f4b59271f8e9362f2f86f1a0c1b3b887))
* **filters:** evaluate APRS-IS filter expressions locally ([9e8f073](https://github.com/APRS-NET/APRS.NET/commit/9e8f073bafdea77dac8c30b93474978ae76ac41e))
* **gateway:** de-duplicate packets and replay recent history on login ([d9eeff6](https://github.com/APRS-NET/APRS.NET/commit/d9eeff62b95ba9f5d54938b72eee3e797acffeb1))
* **gateway:** de-duplicate packets and replay recent history on login ([34d4bb8](https://github.com/APRS-NET/APRS.NET/commit/34d4bb8aafdcf687fc9bf99e82681b7383b6dffb))
* **gateway:** report clients, logins and packets as metrics ([373f573](https://github.com/APRS-NET/APRS.NET/commit/373f5733189f124eabce146cd4be71e3630cb469))
* **gateway:** report clients, logins and packets as metrics ([6e12de5](https://github.com/APRS-NET/APRS.NET/commit/6e12de5e239179e0f0f8697beb7303e7e2c778bc))
* **gateway:** serve packets to APRS-IS clients with their own filters ([ee54ecd](https://github.com/APRS-NET/APRS.NET/commit/ee54ecdbd0de097f0d83d846e610dd3de0f5ef43))
* **gateway:** serve packets to APRS-IS clients with their own filters ([06b0e61](https://github.com/APRS-NET/APRS.NET/commit/06b0e610c25b3e8da771fcfd1c160ec3905ed9ec))
* **gateway:** track where stations were heard for m/ and f/ filters ([f9ead6b](https://github.com/APRS-NET/APRS.NET/commit/f9ead6bdcf89707aa9c7053766e7c9d4d4257aa9))
* **gateway:** track where stations were heard for m/ and f/ filters ([3c8a745](https://github.com/APRS-NET/APRS.NET/commit/3c8a74570ccfb17751ec1b155db5adb2df7d0eb9))
* **kiss:** connect to a KISS TNC over TCP ([1b81bde](https://github.com/APRS-NET/APRS.NET/commit/1b81bde50c70f9c01576c6e3f9f3e5d2bd8fa445))
* **kiss:** connect to a KISS TNC over TCP ([3ade234](https://github.com/APRS-NET/APRS.NET/commit/3ade23470c25043aec56394495005e326b530583))
* **kiss:** frame and unframe KISS data ([7d2003c](https://github.com/APRS-NET/APRS.NET/commit/7d2003c8bcfdd7f9a75bd7828dbb820f24b8699c))
* **kiss:** frame and unframe KISS data ([c3d13a3](https://github.com/APRS-NET/APRS.NET/commit/c3d13a3bfb7334937a6f5c35676c2c6b17ab9b62))
* **kiss:** reconnect to a TCP TNC that closes or cannot be reached ([45b5c25](https://github.com/APRS-NET/APRS.NET/commit/45b5c25469869036270b7dc178554d5956be549f))
* **kiss:** reconnect to a TCP TNC that closes or cannot be reached ([3688609](https://github.com/APRS-NET/APRS.NET/commit/368860975100962f25a72a90a2f9cee4fe4a09eb))
* **kiss:** speak KISS over any stream, for serial and Bluetooth TNCs ([217909c](https://github.com/APRS-NET/APRS.NET/commit/217909c1a17755c45e786f456238dc0c33fbb05f))
* **kiss:** speak KISS over any stream, for serial and Bluetooth TNCs ([7ccd292](https://github.com/APRS-NET/APRS.NET/commit/7ccd292f73af2331f594818da9a584a44da8530b))
* **packets:** build capabilities and user-defined data with AprsPacket.Create ([cf8a786](https://github.com/APRS-NET/APRS.NET/commit/cf8a7866e8a8496d9ba061c7a33df69af6117c4b))
* **packets:** build capabilities and user-defined data with AprsPacket.Create ([06f3da3](https://github.com/APRS-NET/APRS.NET/commit/06f3da3ed4d2de4fc1dbd917a3628d5fc48a78ab))
* **packets:** build telemetry reports and definitions with AprsPacket.Create ([afa67a4](https://github.com/APRS-NET/APRS.NET/commit/afa67a47b71727af876db508a3f87ca227530d07))
* **packets:** build telemetry reports and definitions with AprsPacket.Create ([bb46c32](https://github.com/APRS-NET/APRS.NET/commit/bb46c3250c127c115ac1098ca3d08cdcbf846a04))
* **packets:** build weather reports with AprsPacket.Create ([7494726](https://github.com/APRS-NET/APRS.NET/commit/749472697c67d5aedddc56b2846c3e1636bfbeb7))
* **packets:** build weather reports with AprsPacket.Create ([ee79495](https://github.com/APRS-NET/APRS.NET/commit/ee7949529df4d16e78f0e76cc789486c191f0a6c))
* **packets:** decode and build third-party traffic ([7a3a722](https://github.com/APRS-NET/APRS.NET/commit/7a3a72202153fdf38bb739e0a1c2261762f2b73f))
* **packets:** read and write base-91 comment telemetry ([62ac9a4](https://github.com/APRS-NET/APRS.NET/commit/62ac9a49330932589f25355bcabf82fd3e4c7026))
* **packets:** read and write base-91 comment telemetry ([9009117](https://github.com/APRS-NET/APRS.NET/commit/900911730a5a9ec4a5790f698b22efad24e09bf6))
* target .NET 10 only ([bec8fbb](https://github.com/APRS-NET/APRS.NET/commit/bec8fbbb1a0f4eeabf29dbc0231ec047fce12418))


### Bug Fixes

* **aprs-is:** name the server and count keepalives in a packet-silence error ([d5e1016](https://github.com/APRS-NET/APRS.NET/commit/d5e101601130f872ea15d31554ece3507b1b3ccd))
* **aprs-is:** never fault AprsIsConnection while it closes ([8ca089c](https://github.com/APRS-NET/APRS.NET/commit/8ca089cb968085392392dc1f4f4e81e13ccbe20d))
* **aprs-is:** never fault AprsIsConnection while it closes ([9b6f1dd](https://github.com/APRS-NET/APRS.NET/commit/9b6f1dd2dc5d07d9542bf2d3c7b5276b49062e1c))
* **cli:** like the libraries. ([fdfb2db](https://github.com/APRS-NET/APRS.NET/commit/fdfb2dbf90652f66f3aecf7e408d89073ffcd59a))
* **packets:** point PositionTooShort at the end of a cut-off packet ([ae7c278](https://github.com/APRS-NET/APRS.NET/commit/ae7c2787221c820f3097c35cf290e1cb04f3b4c4))
* **packets:** point PositionTooShort at the end of a cut-off packet ([40c4ed9](https://github.com/APRS-NET/APRS.NET/commit/40c4ed998f7223cdeb1fb872266d4d1412682c41))
* **packets:** read objects whose names are not padded to 9 ([90f1637](https://github.com/APRS-NET/APRS.NET/commit/90f16379898b021bdcaabc06fea9ce609741c401))
* **packets:** read objects whose names are not padded to 9 ([407a7e6](https://github.com/APRS-NET/APRS.NET/commit/407a7e6bc8c1341e1f8123db1dd7c765869a0101))
* **packets:** read positionless weather with an unreadable timestamp ([1c91eda](https://github.com/APRS-NET/APRS.NET/commit/1c91edac6df9a11f416633ecd5f1e4a44641d102))


### Performance Improvements

* **filters:** work out packet facts on demand and match without LINQ ([28df04c](https://github.com/APRS-NET/APRS.NET/commit/28df04cdd48609eeb0d9aef9fde9cf22adc8eea8))


### Documentation

* describe the 0.2.0 milestone in the README ([6a1bed6](https://github.com/APRS-NET/APRS.NET/commit/6a1bed6d65de29d8625179a6c2ce21b6d56f9a70))

## [0.1.0-alpha.17](https://github.com/APRS-NET/APRS.NET/compare/v0.1.0-alpha.16...v0.1.0-alpha.17) (2026-10-15)


### Features

* **aprs-is:** reconnect automatically with AprsIsConnection ([b332851](https://github.com/APRS-NET/APRS.NET/commit/b332851748e80a76f3dced23fb08bcb082b33bad))
* **aprs-is:** reconnect automatically with AprsIsConnection ([63263c3](https://github.com/APRS-NET/APRS.NET/commit/63263c3e1d87f0f766908be8efd21242e128cf4f))

## [0.1.0-alpha.16](https://github.com/APRS-NET/APRS.NET/compare/v0.1.0-alpha.15...v0.1.0-alpha.16) (2026-10-15)


### Features

* **aprs-is:** send packets and messages on a verified login ([9207d0e](https://github.com/APRS-NET/APRS.NET/commit/9207d0e0e835f31b68852369e7e07b09dbcfd93c))
* **aprs-is:** send packets and messages on a verified login ([ac4546c](https://github.com/APRS-NET/APRS.NET/commit/ac4546c72ce23789183f6247adccbacbc0db33f3))

## [0.1.0-alpha.15](https://github.com/APRS-NET/APRS.NET/compare/v0.1.0-alpha.14...v0.1.0-alpha.15) (2026-10-14)


### Features

* give validation errors stable codes for translation ([1db4a47](https://github.com/APRS-NET/APRS.NET/commit/1db4a476dab5352739987bbff76e7b6855c54be8))
* give validation errors stable codes for translation ([6c373e6](https://github.com/APRS-NET/APRS.NET/commit/6c373e6c5bbd78bf04746698b4cfd0ad1216513b))

## [0.1.0-alpha.14](https://github.com/APRS-NET/APRS.NET/compare/v0.1.0-alpha.13...v0.1.0-alpha.14) (2026-10-14)


### Features

* **packets:** build Mic-E reports with AprsPacket.Create ([bb94099](https://github.com/APRS-NET/APRS.NET/commit/bb94099c1de3cf271b6b672ad0d8ecc9d0990e33))
* **packets:** build Mic-E reports with AprsPacket.Create ([f6ec72b](https://github.com/APRS-NET/APRS.NET/commit/f6ec72b665aaac36f9fd1bac79b8fae14a84c889))
* **packets:** build objects and items with AprsPacket.Create ([acde37e](https://github.com/APRS-NET/APRS.NET/commit/acde37e2cc1017026fa0bae7a0c899990b0bf418))
* **packets:** build objects and items with AprsPacket.Create ([1b04515](https://github.com/APRS-NET/APRS.NET/commit/1b045156a76bf0a1bc1259eaa38e388bc2d5a8b3))

## [0.1.0-alpha.13](https://github.com/APRS-NET/APRS.NET/compare/v0.1.0-alpha.12...v0.1.0-alpha.13) (2026-10-14)


### Features

* **packets:** send as one station with PacketAddress ([7fc9c44](https://github.com/APRS-NET/APRS.NET/commit/7fc9c44fd81455400dd202106789be6b8e2a42a7))
* **packets:** send as one station with PacketAddress ([b82f71b](https://github.com/APRS-NET/APRS.NET/commit/b82f71b6a268d28de23b4d5acc9a7b450c818084))

## [0.1.0-alpha.12](https://github.com/APRS-NET/APRS.NET/compare/v0.1.0-alpha.11...v0.1.0-alpha.12) (2026-10-14)


### Features

* **aprs-is:** list the aprs2.net rotates as server presets ([eeae65b](https://github.com/APRS-NET/APRS.NET/commit/eeae65b8205ecd9b83256f61f311ea3f2851cced))
* **aprs-is:** list the aprs2.net rotates as server presets ([74d78a6](https://github.com/APRS-NET/APRS.NET/commit/74d78a618972507db04124333c0bfbf34cc2592e))
* **packets:** build position reports with AprsPacket.Create ([5c068c3](https://github.com/APRS-NET/APRS.NET/commit/5c068c3d45c92ec7c3e54ba79e3365a1b65bc297))
* **packets:** build status reports and messages with AprsPacket.Create ([513ce70](https://github.com/APRS-NET/APRS.NET/commit/513ce70544435334ee31aa938d468e4cfcf50371))
* **packets:** convert Maidenhead locators to and from positions ([363ebf1](https://github.com/APRS-NET/APRS.NET/commit/363ebf1daa76aa5c88721878fbab6232bcc74ade))
* **packets:** convert Maidenhead locators to and from positions ([b455771](https://github.com/APRS-NET/APRS.NET/commit/b45577161b1d25723b5c7be6b10e33acf10b7d44))

## [0.1.0-alpha.11](https://github.com/APRS-NET/APRS.NET/compare/v0.1.0-alpha.10...v0.1.0-alpha.11) (2026-10-14)


### Features

* **packets:** decode station capabilities, user-defined data and raw weather ([cf7b210](https://github.com/APRS-NET/APRS.NET/commit/cf7b210dedaf84f203e8be25b3d38fb87e56b6ce))

## [0.1.0-alpha.10](https://github.com/APRS-NET/APRS.NET/compare/v0.1.0-alpha.9...v0.1.0-alpha.10) (2026-10-14)


### Features

* **aprs-is:** compute APRS-IS passcodes ([f6658ae](https://github.com/APRS-NET/APRS.NET/commit/f6658ae05575333e4d55f032419434023df3f24a))

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
