# Changelog

## [0.7.0-alpha.1](https://github.com/APRS-NET/APRS.NET/compare/v0.6.0-alpha.5...v0.7.0-alpha.1) (2026-10-26)


### Miscellaneous Chores

* the README and package descriptions cover the 0.7.0 work ([c1af5b6](https://github.com/APRS-NET/APRS.NET/commit/c1af5b6a332ed30f2efd38bfaaeca9c67655f291))

## [0.6.0-alpha.5](https://github.com/APRS-NET/APRS.NET/compare/v0.6.0-alpha.4...v0.6.0-alpha.5) (2026-10-26)


### Features

* **ax25:** digipeater path rules ([bad82ae](https://github.com/APRS-NET/APRS.NET/commit/bad82ae27b516d31bd9a8cd7230084da50c4eef7)), closes [#210](https://github.com/APRS-NET/APRS.NET/issues/210)
* **cli:** /health says when a TNC has gone quiet ([3f17544](https://github.com/APRS-NET/APRS.NET/commit/3f17544b8777f43ca11dc3ad1bb54cdac29b416e)), closes [#322](https://github.com/APRS-NET/APRS.NET/issues/322)
* **cli:** serve --igate-to-rf runs a two-way IGate ([2ce2a04](https://github.com/APRS-NET/APRS.NET/commit/2ce2a04cf2436305aa956b6eafd3398118308ded)), closes [#215](https://github.com/APRS-NET/APRS.NET/issues/215)
* **cli:** tnc digi runs a digipeater on a KISS TNC ([4b593af](https://github.com/APRS-NET/APRS.NET/commit/4b593afa24feeca13915ffe7d4afb23efc7a7724)), closes [#211](https://github.com/APRS-NET/APRS.NET/issues/211)
* **cli:** tnc listen --all-ports reads every port of a TNC ([24b6c05](https://github.com/APRS-NET/APRS.NET/commit/24b6c056bcde35c29287b20e9290e4b97ce86873)), closes [#222](https://github.com/APRS-NET/APRS.NET/issues/222)
* **gateway:** a quiet source can count as down in health checks ([53c5251](https://github.com/APRS-NET/APRS.NET/commit/53c5251c997e0917607aa5a46126bbe0daad5a13)), closes [#322](https://github.com/APRS-NET/APRS.NET/issues/322)
* **igate:** a list of stations heard on RF ([7dfdce7](https://github.com/APRS-NET/APRS.NET/commit/7dfdce79623dc3f1f047ec8851276ee0794c0430)), closes [#213](https://github.com/APRS-NET/APRS.NET/issues/213)
* **igate:** gate messages from APRS-IS to RF ([cd9381c](https://github.com/APRS-NET/APRS.NET/commit/cd9381ceced6899a965df8fbb865525bbcdfaf1c)), closes [#214](https://github.com/APRS-NET/APRS.NET/issues/214)
* **kiss:** one connection for every port of a multi-port TNC ([753564b](https://github.com/APRS-NET/APRS.NET/commit/753564b847089ac81ccb86cdcdb32e932d2bc718)), closes [#222](https://github.com/APRS-NET/APRS.NET/issues/222)
* **packets:** count the digipeater hops in a packet's path ([be9b8ff](https://github.com/APRS-NET/APRS.NET/commit/be9b8ffb7cfac1206d0c798a1c4d0f33b09f0427)), closes [#213](https://github.com/APRS-NET/APRS.NET/issues/213)


### Bug Fixes

* **cli:** a missing or unplugged serial GPS is called a GPS ([8568abd](https://github.com/APRS-NET/APRS.NET/commit/8568abd225e453949607c6a5a7a3f14b9f290dab)), closes [#308](https://github.com/APRS-NET/APRS.NET/issues/308)
* **gateway:** a server stopped right after it starts no longer throws ([a639525](https://github.com/APRS-NET/APRS.NET/commit/a6395251624a28acb792cfa040484b9d9100c5ff))
* **kiss:** KissTcpServer reports a refused app before closing it ([339d7d4](https://github.com/APRS-NET/APRS.NET/commit/339d7d41732d09b5d9b1c4de7f9f793432fb24b4))

## [0.6.0-alpha.4](https://github.com/APRS-NET/APRS.NET/compare/v0.6.0-alpha.3...v0.6.0-alpha.4) (2026-10-25)


### Features

* **cli:** tnc listen and serve say when a radio sends GPS ([a7656b2](https://github.com/APRS-NET/APRS.NET/commit/a7656b2ab78654ce4eeb6710d7d0edf44886a429)), closes [#320](https://github.com/APRS-NET/APRS.NET/issues/320)
* **kiss:** a radio's GPS sentences in text mode ([ddc3708](https://github.com/APRS-NET/APRS.NET/commit/ddc3708e80b8fa42dfc135601b681e9bbb7c80aa)), closes [#320](https://github.com/APRS-NET/APRS.NET/issues/320)

## [0.6.0-alpha.3](https://github.com/APRS-NET/APRS.NET/compare/v0.6.0-alpha.2...v0.6.0-alpha.3) (2026-10-22)


### Features

* **cli:** serve and tnc share log their events for OpenTelemetry ([2499356](https://github.com/APRS-NET/APRS.NET/commit/24993560d89777da22c6ef06c6871c2816411369)), closes [#316](https://github.com/APRS-NET/APRS.NET/issues/316)
* **cli:** serve and tnc share read their options from a TOML file ([b2ca218](https://github.com/APRS-NET/APRS.NET/commit/b2ca218e628d4a5fa758eac134836bc9b6761f58))
* **kiss:** KissTcpServer counts its apps and frames ([e5fab65](https://github.com/APRS-NET/APRS.NET/commit/e5fab6520b7253d39cd3bf1bb8df30d7f8ce4c47)), closes [#316](https://github.com/APRS-NET/APRS.NET/issues/316)


### Bug Fixes

* **deps:** Bump the opentelemetry group with 2 updates ([17c8d0d](https://github.com/APRS-NET/APRS.NET/commit/17c8d0dc73ae3e69776612ee0744b5fe811cfea7))

## [0.6.0-alpha.2](https://github.com/APRS-NET/APRS.NET/compare/v0.6.0-alpha.1...v0.6.0-alpha.2) (2026-10-22)


### Features

* **cli:** aprscli tnc share serves a TNC to KISS apps over TCP ([f116e67](https://github.com/APRS-NET/APRS.NET/commit/f116e67ec0ada0c3f16a154f756086dbf7629719))
* **kiss:** KissTcpServer reports apps connecting, leaving and turned away ([9aae663](https://github.com/APRS-NET/APRS.NET/commit/9aae66334c02895a13fb717807b3bf534e2460d8))

## [0.6.0-alpha.1](https://github.com/APRS-NET/APRS.NET/compare/v0.5.0-alpha.2...v0.6.0-alpha.1) (2026-10-21)


### Bug Fixes

* **cli:** aprscli gps prints a fix gpsd repeats only once ([cae82c3](https://github.com/APRS-NET/APRS.NET/commit/cae82c3feee73ab7f2866cc8af195a22062e6ab2)), closes [#205](https://github.com/APRS-NET/APRS.NET/issues/205)

## [0.5.0-alpha.2](https://github.com/APRS-NET/APRS.NET/compare/v0.5.0-alpha.1...v0.5.0-alpha.2) (2026-10-21)


### Features

* **cli:** aprscli gps prints a GPS's fixes from gpsd or NMEA over TCP ([1c0174b](https://github.com/APRS-NET/APRS.NET/commit/1c0174b5519a2214ed5337eec525697989fa2a85))
* **cli:** aprscli gps reads a GPS on a serial port ([d2540b2](https://github.com/APRS-NET/APRS.NET/commit/d2540b2c151d29f8ac183c29603c33204e924c04))
* **cli:** send beacon keeps a station on the map ([e6a0921](https://github.com/APRS-NET/APRS.NET/commit/e6a092112ecbe3a914c62224b13563e4ac72948f)), closes [#199](https://github.com/APRS-NET/APRS.NET/issues/199)
* **deviceid:** load and fetch a newer device list ([a85599f](https://github.com/APRS-NET/APRS.NET/commit/a85599f2443d7a9c46bdf653a500ca269a12821e)), closes [#280](https://github.com/APRS-NET/APRS.NET/issues/280)
* **gps:** satellites, DOPs and error estimates for a GPS status view ([4680b87](https://github.com/APRS-NET/APRS.NET/commit/4680b876ed8a8194408209ed46ff2e42b531ee48)), closes [#299](https://github.com/APRS-NET/APRS.NET/issues/299)

## [0.5.0-alpha.1](https://github.com/APRS-NET/APRS.NET/compare/v0.4.0-alpha.12...v0.5.0-alpha.1) (2026-10-21)


### Features

* **beacons:** SmartBeaconing schedule for moving stations ([5728ea2](https://github.com/APRS-NET/APRS.NET/commit/5728ea2f808b3fbf0d3484a1257629fede58b9c4)), closes [#197](https://github.com/APRS-NET/APRS.NET/issues/197)


### Bug Fixes

* **cli:** mention ,text in the --tnc help ([07a1573](https://github.com/APRS-NET/APRS.NET/commit/07a1573c05016f59b8bce8983fa1c5666ea914d0))

## [0.4.0-alpha.12](https://github.com/APRS-NET/APRS.NET/compare/v0.4.0-alpha.11...v0.4.0-alpha.12) (2026-10-20)


### Features

* **aprs-is:** send packets an IGate gated from RF ([0a9c8dc](https://github.com/APRS-NET/APRS.NET/commit/0a9c8dcfd5efb13d996ed6b5ffcda3f6a189da55))
* **beacons:** dead reckoning from the last beacon ([54d4713](https://github.com/APRS-NET/APRS.NET/commit/54d47138eb3bb723eca3947a4eade62e4a25f08a)), closes [#198](https://github.com/APRS-NET/APRS.NET/issues/198)
* **cli:** serve --igate as a receive-only IGate ([b7c6613](https://github.com/APRS-NET/APRS.NET/commit/b7c6613056113326fa86f317b003cecd41f6ece1))
* **deviceid:** update the device list to aprs-deviceid be890f1 ([658e706](https://github.com/APRS-NET/APRS.NET/commit/658e706be72d318364d9af8dc3bbaa7a31a3a0f5))
* **gateway:** counters on the health report ([ed17bb4](https://github.com/APRS-NET/APRS.NET/commit/ed17bb43aa98b8a359b2ed1fbac41cbbb36381d0))
* **gps:** read a GPS through gpsd with AprsNet.Gps ([9cd5a93](https://github.com/APRS-NET/APRS.NET/commit/9cd5a938d3f2fd2fd97418b9328551e84caabce2)), closes [#127](https://github.com/APRS-NET/APRS.NET/issues/127)
* **igate:** RF-to-internet gating rules in AprsNet.IGate ([dccb41d](https://github.com/APRS-NET/APRS.NET/commit/dccb41d2b6fd8d39fc91a90d7b813b8498106cbc)), closes [#207](https://github.com/APRS-NET/APRS.NET/issues/207)
* **messaging:** answer APRS queries with QueryResponder ([2fbd157](https://github.com/APRS-NET/APRS.NET/commit/2fbd15778d998b6534db61c5854a6fce06f6395f))
* **packets:** general queries as QueryReport ([4d2d3c1](https://github.com/APRS-NET/APRS.NET/commit/4d2d3c103641fdf8176d9544d1a21afec2cdbc04))
* **packets:** NMEA sentences, GPS fixes and $ packets ([1e2aadb](https://github.com/APRS-NET/APRS.NET/commit/1e2aadbe7a326c45e4fccf57006704b543dbdbda)), closes [#204](https://github.com/APRS-NET/APRS.NET/issues/204)

## [0.4.0-alpha.11](https://github.com/APRS-NET/APRS.NET/compare/v0.4.0-alpha.10...v0.4.0-alpha.11) (2026-10-19)


### Features

* **deviceid:** update the device list to aprs-deviceid 368f5b8 ([aec4b7a](https://github.com/APRS-NET/APRS.NET/commit/aec4b7a4dd36f681c27acbb9c2a3631b8f62ae75))

## [0.4.0-alpha.10](https://github.com/APRS-NET/APRS.NET/compare/v0.4.0-alpha.9...v0.4.0-alpha.10) (2026-10-18)


### Features

* **cli:** show what comes back while a message runs ([f8e94a8](https://github.com/APRS-NET/APRS.NET/commit/f8e94a85154550b9e8fc7fcf4a9f8c9c8d412fbb))

## [0.4.0-alpha.9](https://github.com/APRS-NET/APRS.NET/compare/v0.4.0-alpha.8...v0.4.0-alpha.9) (2026-10-18)


### Features

* **messaging:** a bulletin repeat schedule and ANSRVR commands ([2b62ea9](https://github.com/APRS-NET/APRS.NET/commit/2b62ea99fe34891b856ea3a327291004ec1b9398))

## [0.4.0-alpha.8](https://github.com/APRS-NET/APRS.NET/compare/v0.4.0-alpha.7...v0.4.0-alpha.8) (2026-10-18)


### Bug Fixes

* **kiss:** treat text whose first letter looks like a command as text ([d5bf9df](https://github.com/APRS-NET/APRS.NET/commit/d5bf9df3879b9d5dd9bd4a57af05252eaa2ebbc2)), closes [#269](https://github.com/APRS-NET/APRS.NET/issues/269)

## [0.4.0-alpha.7](https://github.com/APRS-NET/APRS.NET/compare/v0.4.0-alpha.6...v0.4.0-alpha.7) (2026-10-18)


### Features

* **aprs-is:** report every line to and from the server as Traffic ([ffd1b49](https://github.com/APRS-NET/APRS.NET/commit/ffd1b4928f54f8f6dd046f213a5f654e745b48bd)), closes [#262](https://github.com/APRS-NET/APRS.NET/issues/262)
* **cli:** read a radio in APRS mode with ,text in a serial address ([ea6e6fd](https://github.com/APRS-NET/APRS.NET/commit/ea6e6fda8e5d891fd10a5b352986cf341f21b152))
* **kiss:** read a radio in its APRS mode as text ([e20ba6a](https://github.com/APRS-NET/APRS.NET/commit/e20ba6ab7c762ed9e7c17afde61a7b8f5eeb32a9))
* **kiss:** take bytes that don't start with a command byte as text ([82a1298](https://github.com/APRS-NET/APRS.NET/commit/82a1298d365fed9464972be80df1e66664db4f78))
* **kiss:** type to a TNC's command line over an open connection ([8c17305](https://github.com/APRS-NET/APRS.NET/commit/8c173054debcc77001e6d569520cc772b59e8039))

## [0.4.0-alpha.6](https://github.com/APRS-NET/APRS.NET/compare/v0.4.0-alpha.5...v0.4.0-alpha.6) (2026-10-18)


### Features

* **cli:** switch a radio's TNC into KISS, and a console on its command line ([c50db29](https://github.com/APRS-NET/APRS.NET/commit/c50db29dcadaa7d4e2b3a6955c53948215f93ff2))
* **kiss:** add a drain step that throws away what waited in the port ([577888c](https://github.com/APRS-NET/APRS.NET/commit/577888c038a272c345ad51b3020dde39982dc399))
* **kiss:** report everything that crosses a KISS link as Traffic ([9ca8753](https://github.com/APRS-NET/APRS.NET/commit/9ca8753ea926f2a87571e5bd2b0d3eb40fa8b902))
* **kiss:** switch a radio's built-in TNC into KISS with a TncSetup ([7fc65ff](https://github.com/APRS-NET/APRS.NET/commit/7fc65ff93c45b75c59b6dc36096e4531dd267b61))

## [0.4.0-alpha.5](https://github.com/APRS-NET/APRS.NET/compare/v0.4.0-alpha.4...v0.4.0-alpha.5) (2026-10-18)


### Features

* **aprs-is:** give a login that reaches a KISS TNC its own exception ([d551ba1](https://github.com/APRS-NET/APRS.NET/commit/d551ba1e7447a1e8f2093c82ee60a45975ff52c7)), closes [#246](https://github.com/APRS-NET/APRS.NET/issues/246)
* **cli:** stop and point to tnc listen when a server is a KISS TNC ([322721d](https://github.com/APRS-NET/APRS.NET/commit/322721d0c06dc5d0c215ebc73714355594edad39)), closes [#246](https://github.com/APRS-NET/APRS.NET/issues/246)
* **cli:** take dtr=off and rts=off in a serial TNC address ([1c6134c](https://github.com/APRS-NET/APRS.NET/commit/1c6134ceabc657ef996d95deb67b619005af4d1c)), closes [#126](https://github.com/APRS-NET/APRS.NET/issues/126)
* **corpus:** read, write and anonymize corpus captures with AprsNet.Corpus ([1b0d322](https://github.com/APRS-NET/APRS.NET/commit/1b0d32229a5fe9ff05bb4e57678b61a10f35f3cf))
* **gateway:** count a TNC sending text as a protocol mismatch, not up ([f03d9e1](https://github.com/APRS-NET/APRS.NET/commit/f03d9e12ac41b52c171240e69f842d3c7d7e7539)), closes [#254](https://github.com/APRS-NET/APRS.NET/issues/254)
* **kiss:** let a serial TNC leave DTR and RTS low ([1622ba2](https://github.com/APRS-NET/APRS.NET/commit/1622ba25e4974ad2f21a43f619a4e48b6f6f9870)), closes [#126](https://github.com/APRS-NET/APRS.NET/issues/126)
* **kiss:** say how long a TNC has been quiet ([9931fda](https://github.com/APRS-NET/APRS.NET/commit/9931fdabbf4fa672286ca73c6b075cbbe7d8b0df)), closes [#255](https://github.com/APRS-NET/APRS.NET/issues/255)
* **packets:** list the parts of a packet that parsed but weren't understood ([35b88af](https://github.com/APRS-NET/APRS.NET/commit/35b88af3af46a3d0df3d1c611ae4217c96605aa4))

## [0.4.0-alpha.4](https://github.com/APRS-NET/APRS.NET/compare/v0.4.0-alpha.3...v0.4.0-alpha.4) (2026-10-17)


### Features

* **aprs-is:** say so when a login reaches a KISS TNC ([7bb2078](https://github.com/APRS-NET/APRS.NET/commit/7bb20781edf9d5ebf9134264af409984d84e63b0)), closes [#232](https://github.com/APRS-NET/APRS.NET/issues/232)
* **cli:** add --quiet-warning to tnc listen and serve ([8ee36dd](https://github.com/APRS-NET/APRS.NET/commit/8ee36ddc421feb65acc097c335714b2a7fe0b597)), closes [#234](https://github.com/APRS-NET/APRS.NET/issues/234)
* **cli:** hold a message conversation through a TNC ([8f9b0af](https://github.com/APRS-NET/APRS.NET/commit/8f9b0afc8f2ec8958708d66546a462eadc9b895d)), closes [#235](https://github.com/APRS-NET/APRS.NET/issues/235)
* **cli:** print a TNC's warnings in tnc listen and serve ([cf10aee](https://github.com/APRS-NET/APRS.NET/commit/cf10aee959d8f11eed9bdc52b75f8ab1e0d1cb5b)), closes [#232](https://github.com/APRS-NET/APRS.NET/issues/232)
* **cli:** say when a lost TNC is back ([3a560c2](https://github.com/APRS-NET/APRS.NET/commit/3a560c29ba5d4673790bd4034b93f5bf48833e15))
* **gateway:** show a quiet source in the health report ([cf97d29](https://github.com/APRS-NET/APRS.NET/commit/cf97d29ce8858de658c409f3a8f0f7168f05df7c)), closes [#234](https://github.com/APRS-NET/APRS.NET/issues/234)
* **kiss:** warn when a TNC goes quiet behind a port that stays open ([d1896af](https://github.com/APRS-NET/APRS.NET/commit/d1896af8e65eeabb2b3c1ddd3c0f34e6b258e785)), closes [#234](https://github.com/APRS-NET/APRS.NET/issues/234)
* **kiss:** warn when a TNC sends text instead of KISS frames ([e46a79a](https://github.com/APRS-NET/APRS.NET/commit/e46a79aafe2f17cf3014d7cf93d62b54069f3e9b)), closes [#232](https://github.com/APRS-NET/APRS.NET/issues/232)


### Bug Fixes

* **kiss:** land the serial TNC stack on main ([bd99641](https://github.com/APRS-NET/APRS.NET/commit/bd996410dd48746b7d9007092f463c4611653845))
* **kiss:** take an unplugged serial TNC for a lost link ([47bb12a](https://github.com/APRS-NET/APRS.NET/commit/47bb12a42249884e4fb45290fd2357228d1bbcaa))

## [0.4.0-alpha.3](https://github.com/APRS-NET/APRS.NET/compare/v0.4.0-alpha.2...v0.4.0-alpha.3) (2026-10-17)


### Features

* **aprs-is:** log in without a callsign as a listener ([122744e](https://github.com/APRS-NET/APRS.NET/commit/122744e595b818e4145dfe1543d3c5182f079db4)), closes [#227](https://github.com/APRS-NET/APRS.NET/issues/227)
* **beacons:** answer when to beacon next with AprsNet.Beacons ([886c290](https://github.com/APRS-NET/APRS.NET/commit/886c2900d87d16c45bbd2eb47708226086135243)), closes [#196](https://github.com/APRS-NET/APRS.NET/issues/196)
* **cli:** listen and serve as a listener when no callsign is given ([1afc36d](https://github.com/APRS-NET/APRS.NET/commit/1afc36d9da3a4997ce9d4e966f87b9e96680cb2c)), closes [#227](https://github.com/APRS-NET/APRS.NET/issues/227)
* **messaging:** report each retry of an outgoing message ([1904851](https://github.com/APRS-NET/APRS.NET/commit/1904851eb9b282be1d787b73f4a2ed98796f2102)), closes [#231](https://github.com/APRS-NET/APRS.NET/issues/231)


### Bug Fixes

* **cli:** show a message's ID in send --dry-run ([cf3588c](https://github.com/APRS-NET/APRS.NET/commit/cf3588c8b985c19e1eb225f6de6ef8284279c427)), closes [#194](https://github.com/APRS-NET/APRS.NET/issues/194)
* **kiss:** raise DTR and RTS when opening a serial TNC ([2f33c63](https://github.com/APRS-NET/APRS.NET/commit/2f33c6398883b773ca254874784ed132f9c5ddbd))

## [0.4.0-alpha.2](https://github.com/APRS-NET/APRS.NET/compare/v0.4.0-alpha.1...v0.4.0-alpha.2) (2026-10-17)


### ⚠ BREAKING CHANGES

* **aprs-is:** AprsMessenger, MessageDelivery and MessageDeliveryStatus are removed from AprsNet.AprsIsTcpClient; use MessagingStation from AprsNet.Messaging.

### Features

* **aprs-is:** remove AprsMessenger in favour of AprsNet.Messaging ([ea810a7](https://github.com/APRS-NET/APRS.NET/commit/ea810a7d33a5d0da06ecfc14ffea72597c1af75b))
* **cli:** send messages through MessagingStation, with --retry ([1b14c52](https://github.com/APRS-NET/APRS.NET/commit/1b14c52efd4fdaf623522348f2bcb9805111d989))
* **messaging:** hold APRS message conversations with AprsNet.Messaging ([9b3d3dd](https://github.com/APRS-NET/APRS.NET/commit/9b3d3dd78608e3b2dfbe3550e961b2fdb4dcd5b8))
* **messaging:** keep the bulletins and announcements heard ([d04a94a](https://github.com/APRS-NET/APRS.NET/commit/d04a94a60dccb4a21fe6ee3b921d7bd2d2ede978)), closes [#193](https://github.com/APRS-NET/APRS.NET/issues/193)

## [0.4.0-alpha.1](https://github.com/APRS-NET/APRS.NET/compare/v0.3.0-alpha.3...v0.4.0-alpha.1) (2026-10-16)


### Bug Fixes

* **gateway:** don't miss packets right after a WebSocket greeting ([8642aea](https://github.com/APRS-NET/APRS.NET/commit/8642aeaa7ce0d2a8ece9c313ee0e874709bc58f3))


### Documentation

* start the 0.4.0 alpha line ([6db5df6](https://github.com/APRS-NET/APRS.NET/commit/6db5df64cf0357044f4deabd02ca84f8832a1bcf))

## [0.3.0-alpha.3](https://github.com/APRS-NET/APRS.NET/compare/v0.3.0-alpha.2...v0.3.0-alpha.3) (2026-10-16)


### Bug Fixes

* **kiss:** notice a TNC that went away without closing the link ([4f09982](https://github.com/APRS-NET/APRS.NET/commit/4f09982025b860b407f968d0e23f40d305df87a5))

## [0.3.0-alpha.2](https://github.com/APRS-NET/APRS.NET/compare/v0.3.0-alpha.1...v0.3.0-alpha.2) (2026-10-16)


### Features

* **cli:** check a running gateway with aprscli health ([3eed7f4](https://github.com/APRS-NET/APRS.NET/commit/3eed7f491cd8cacc482585d5ef83a4efb03b4f5d)), closes [#167](https://github.com/APRS-NET/APRS.NET/issues/167)
* **cli:** serve health checks with aprscli serve --http ([4383f9e](https://github.com/APRS-NET/APRS.NET/commit/4383f9e1df1bb1aaa3d03c83e70a056c73a0127f)), closes [#166](https://github.com/APRS-NET/APRS.NET/issues/166)
* **cli:** serve WebSocket clients with aprscli serve --http ([7972f2d](https://github.com/APRS-NET/APRS.NET/commit/7972f2d2413ce5a239112872749a35fdb6fc0f29)), closes [#165](https://github.com/APRS-NET/APRS.NET/issues/165)
* **cli:** use a TNC on a serial port with --tnc serial:PORT ([66f6e93](https://github.com/APRS-NET/APRS.NET/commit/66f6e93005316e0fe9af7b7f881042f018941612)), closes [#163](https://github.com/APRS-NET/APRS.NET/issues/163)
* **gateway:** answer health checks at /health ([5132075](https://github.com/APRS-NET/APRS.NET/commit/513207589dc77d9bfe11d17af5f1f88cb3ec1cfa)), closes [#166](https://github.com/APRS-NET/APRS.NET/issues/166)
* **gateway:** stream packets as JSON to WebSocket clients ([b5902af](https://github.com/APRS-NET/APRS.NET/commit/b5902afa0e0ef37930dd0072ed04d28881da6dfa)), closes [#165](https://github.com/APRS-NET/APRS.NET/issues/165)
* **kiss:** reopen a serial TNC after it is unplugged ([91e1dcc](https://github.com/APRS-NET/APRS.NET/commit/91e1dccc83438ed77c281841679d477232ebea03)), closes [#126](https://github.com/APRS-NET/APRS.NET/issues/126)
* **packets:** apply a station's telemetry definitions to its reports ([38f729b](https://github.com/APRS-NET/APRS.NET/commit/38f729b8d9fd3ef1f1c1333c868a457f373168e5)), closes [#153](https://github.com/APRS-NET/APRS.NET/issues/153)
* **packets:** flag position reports that don't fit a station's track ([1803200](https://github.com/APRS-NET/APRS.NET/commit/1803200e154ec6c143900a3939a1b7dfbbb76bb8)), closes [#154](https://github.com/APRS-NET/APRS.NET/issues/154)
* write packets as documented JSON with AprsNet.Packets.Json ([5ba274b](https://github.com/APRS-NET/APRS.NET/commit/5ba274bd3d3f6208d9d407e889e8cc075139555e)), closes [#164](https://github.com/APRS-NET/APRS.NET/issues/164)


### Documentation

* keep releasing 0.3.0 alphas until the 0.4.0 milestone is done ([20f5892](https://github.com/APRS-NET/APRS.NET/commit/20f5892e71ef2cae87a7cc089bebe2593fdf3eda))

## [0.3.0-alpha.1](https://github.com/APRS-NET/APRS.NET/compare/v0.2.0-alpha.3...v0.3.0-alpha.1) (2026-10-16)


### Documentation

* describe the 0.3.0 milestone in the README ([ce84b13](https://github.com/APRS-NET/APRS.NET/commit/ce84b135a8d43a967545b9d883a7d3650d8623df))

## [0.2.0-alpha.3](https://github.com/APRS-NET/APRS.NET/compare/v0.2.0-alpha.2...v0.2.0-alpha.3) (2026-10-16)


### ⚠ BREAKING CHANGES

* **gateway:** PacketDeduplicator is removed; use GatewayFeed, or DuplicateFilter from AprsNet.Packets.
* **kiss:** KissTcpConnection.ConnectionLost and KissConnectionLostEventArgs are replaced by StateChanged, whose Reconnecting change carries the same error and wait.
* **kiss:** the tncPort, connectTimeout, reconnectDelay, maxReconnectDelay, timeProvider and transmitLimit constructor parameters of KissTcpClient and KissTcpConnection are now KissTcpOptions properties.

### Features

* **aprs-is:** connect to APRS-IS over TLS ([6285c37](https://github.com/APRS-NET/APRS.NET/commit/6285c371c4a1e51235eb22ae6874cfe1fb1e987c)), closes [#110](https://github.com/APRS-NET/APRS.NET/issues/110)
* **cli:** serve what KISS TNCs hear with aprscli serve --tnc ([b002900](https://github.com/APRS-NET/APRS.NET/commit/b0029004056bc7caa7cdd7a3bad75be591ec3342)), closes [#107](https://github.com/APRS-NET/APRS.NET/issues/107)
* **cli:** transmit through a KISS TNC with aprscli tnc send ([eac1920](https://github.com/APRS-NET/APRS.NET/commit/eac19205f718a627c6717f37ef4e4ca6ffed7b80)), closes [#103](https://github.com/APRS-NET/APRS.NET/issues/103)
* **gateway:** drop PacketDeduplicator in favour of GatewayFeed ([4ff89d3](https://github.com/APRS-NET/APRS.NET/commit/4ff89d3a6cf4667e6edbb4c78014a86dc6e07688)), closes [#99](https://github.com/APRS-NET/APRS.NET/issues/99)
* **gateway:** serve packets from several sources at once ([6cc1b79](https://github.com/APRS-NET/APRS.NET/commit/6cc1b794374581915957d5e9b206567abcbc4d6a)), closes [#106](https://github.com/APRS-NET/APRS.NET/issues/106)
* **kiss:** configure TNC clients and connections with KissTcpOptions ([b38a84c](https://github.com/APRS-NET/APRS.NET/commit/b38a84cddf5d62f7021662c91e2f034994ea34e1)), closes [#99](https://github.com/APRS-NET/APRS.NET/issues/99)
* **kiss:** limit how fast frames are sent for transmission ([f1e15ea](https://github.com/APRS-NET/APRS.NET/commit/f1e15ea2fa63c8cd1bdbea81c0447d7ca915fd2c)), closes [#102](https://github.com/APRS-NET/APRS.NET/issues/102)
* **kiss:** report TNC connection state and metrics like APRS-IS ([66ecf7d](https://github.com/APRS-NET/APRS.NET/commit/66ecf7da65998456e9badafb211cb0c10f5c2833)), closes [#99](https://github.com/APRS-NET/APRS.NET/issues/99)
* **kiss:** share a TNC with several KISS apps through KissTcpServer ([4d1ebc9](https://github.com/APRS-NET/APRS.NET/commit/4d1ebc95f84a543045cfd248282f5ef8b85f3031)), closes [#108](https://github.com/APRS-NET/APRS.NET/issues/108)
* publish the AX.25, KISS, gateway and DI packages ([d7d7fac](https://github.com/APRS-NET/APRS.NET/commit/d7d7fac63809a30bc611eb466b1cd6a2ce9de7d2))


### Bug Fixes

* **ax25:** give a default Ax25Address an empty callsign, not null ([627cb3b](https://github.com/APRS-NET/APRS.NET/commit/627cb3b72dd36b15781d950c1066665e747f9be2)), closes [#99](https://github.com/APRS-NET/APRS.NET/issues/99)
* **kiss:** back off from a TNC that drops every connection at once ([f683590](https://github.com/APRS-NET/APRS.NET/commit/f68359080d8e80b58a548e9664e9e6affa670897)), closes [#113](https://github.com/APRS-NET/APRS.NET/issues/113)
* **kiss:** name reconnect causes as APRS-IS does ([6535bfd](https://github.com/APRS-NET/APRS.NET/commit/6535bfd8d7c3524c58a1b4cff213ed315d1ea6ce)), closes [#99](https://github.com/APRS-NET/APRS.NET/issues/99)

## [0.2.0-alpha.2](https://github.com/APRS-NET/APRS.NET/compare/v0.2.0-alpha.1...v0.2.0-alpha.2) (2026-10-15)


### Features

* **deviceid:** identify devices from the aprs-deviceid list, in a new package, AprsNet.DeviceId ([4b5669c](https://github.com/APRS-NET/APRS.NET/commit/4b5669cf97cadf1b9c1e7bd9c9d1362eb7249e15)), closes [#119](https://github.com/APRS-NET/APRS.NET/issues/119)
* **filters:** build APRS-IS filters from typed terms ([77fd742](https://github.com/APRS-NET/APRS.NET/commit/77fd742467c0b8d85959c12933ad4eab2a290f5c)), closes [#117](https://github.com/APRS-NET/APRS.NET/issues/117)
* **filters:** publish AprsNet.Filters ([5ca6567](https://github.com/APRS-NET/APRS.NET/commit/5ca65678ba7dff3ba6d5b1a37448980ad4e758a8))
* **packets:** add geodesy and unit conversions ([66deffb](https://github.com/APRS-NET/APRS.NET/commit/66deffb169131b5dd79454b7808e99c83927ec2f)), closes [#120](https://github.com/APRS-NET/APRS.NET/issues/120)
* **packets:** analyse how a packet travelled from its path ([1650f8a](https://github.com/APRS-NET/APRS.NET/commit/1650f8a2ef9301c7d48a2925ea5d3ceb5b5e5ab5)), closes [#121](https://github.com/APRS-NET/APRS.NET/issues/121)
* **packets:** detect duplicate packets ([3f8e5ca](https://github.com/APRS-NET/APRS.NET/commit/3f8e5ca72b441d7cbc49de14259a1466801c10b2)), closes [#118](https://github.com/APRS-NET/APRS.NET/issues/118)
* **packets:** merge packet sources and report duplicates by source ([5d14e32](https://github.com/APRS-NET/APRS.NET/commit/5d14e325c4d375850568e4eb7fb71bf868761462)), closes [#125](https://github.com/APRS-NET/APRS.NET/issues/125)

## [0.2.0-alpha.1](https://github.com/APRS-NET/APRS.NET/compare/v0.1.0-alpha.17...v0.2.0-alpha.1) (2026-10-15)


### ⚠ BREAKING CHANGES

* **packets:** WeatherReport.Timestamp is now AprsTimestamp? instead of AprsTimestamp.
* the packages no longer support net8.0; apps on .NET 8 stay on 0.1.0-alpha.17 or earlier.

### Features

* **aprs-is:** count messages to the login callsign, active connections and bytes sent ([60545da](https://github.com/APRS-NET/APRS.NET/commit/60545dacc1a7591436c3fe21062e2ed7af7299c0))
* **aprs-is:** count parse failures by reason ([954a310](https://github.com/APRS-NET/APRS.NET/commit/954a310113dec192f914e9537b80b91ae0e4decb))
* **aprs-is:** make every AprsIsClientOptions setting settable ([0f77d1d](https://github.com/APRS-NET/APRS.NET/commit/0f77d1d8507aa69d22a2b34f5370415f2d4fd88a))
* **aprs-is:** read parsed packets with ReadPacketsAsync ([77ed23d](https://github.com/APRS-NET/APRS.NET/commit/77ed23d7ae389149a2f2490c881a711be7cd3f2a))
* **ax25:** decode and encode AX.25 UI frames ([f701526](https://github.com/APRS-NET/APRS.NET/commit/f701526ed3468b76604366bd476b7869dc7629d3))
* **di:** register an APRS-IS connection from configuration ([9f7a113](https://github.com/APRS-NET/APRS.NET/commit/9f7a113c319b21cb70f55190e9c791e5f5acfcda))
* **filters:** evaluate APRS-IS filter expressions locally ([9e8f073](https://github.com/APRS-NET/APRS.NET/commit/9e8f073bafdea77dac8c30b93474978ae76ac41e))
* **gateway:** de-duplicate packets and replay recent history on login ([34d4bb8](https://github.com/APRS-NET/APRS.NET/commit/34d4bb8aafdcf687fc9bf99e82681b7383b6dffb))
* **gateway:** report clients, logins and packets as metrics ([6e12de5](https://github.com/APRS-NET/APRS.NET/commit/6e12de5e239179e0f0f8697beb7303e7e2c778bc))
* **gateway:** serve packets to APRS-IS clients with their own filters ([06b0e61](https://github.com/APRS-NET/APRS.NET/commit/06b0e610c25b3e8da771fcfd1c160ec3905ed9ec))
* **gateway:** track where stations were heard for m/ and f/ filters ([3c8a745](https://github.com/APRS-NET/APRS.NET/commit/3c8a74570ccfb17751ec1b155db5adb2df7d0eb9))
* **kiss:** connect to a KISS TNC over TCP ([3ade234](https://github.com/APRS-NET/APRS.NET/commit/3ade23470c25043aec56394495005e326b530583))
* **kiss:** frame and unframe KISS data ([c3d13a3](https://github.com/APRS-NET/APRS.NET/commit/c3d13a3bfb7334937a6f5c35676c2c6b17ab9b62))
* **kiss:** reconnect to a TCP TNC that closes or cannot be reached ([3688609](https://github.com/APRS-NET/APRS.NET/commit/368860975100962f25a72a90a2f9cee4fe4a09eb))
* **kiss:** speak KISS over any stream, for serial and Bluetooth TNCs ([7ccd292](https://github.com/APRS-NET/APRS.NET/commit/7ccd292f73af2331f594818da9a584a44da8530b))
* **packets:** build capabilities and user-defined data with AprsPacket.Create ([06f3da3](https://github.com/APRS-NET/APRS.NET/commit/06f3da3ed4d2de4fc1dbd917a3628d5fc48a78ab))
* **packets:** build telemetry reports and definitions with AprsPacket.Create ([bb46c32](https://github.com/APRS-NET/APRS.NET/commit/bb46c3250c127c115ac1098ca3d08cdcbf846a04))
* **packets:** build weather reports with AprsPacket.Create ([ee79495](https://github.com/APRS-NET/APRS.NET/commit/ee7949529df4d16e78f0e76cc789486c191f0a6c))
* **packets:** decode and build third-party traffic ([7a3a722](https://github.com/APRS-NET/APRS.NET/commit/7a3a72202153fdf38bb739e0a1c2261762f2b73f))
* **packets:** read and write base-91 comment telemetry ([9009117](https://github.com/APRS-NET/APRS.NET/commit/900911730a5a9ec4a5790f698b22efad24e09bf6))
* target .NET 10 only ([bec8fbb](https://github.com/APRS-NET/APRS.NET/commit/bec8fbbb1a0f4eeabf29dbc0231ec047fce12418))


### Bug Fixes

* **aprs-is:** name the server and count keepalives in a packet-silence error ([d5e1016](https://github.com/APRS-NET/APRS.NET/commit/d5e101601130f872ea15d31554ece3507b1b3ccd))
* **aprs-is:** never fault AprsIsConnection while it closes ([9b6f1dd](https://github.com/APRS-NET/APRS.NET/commit/9b6f1dd2dc5d07d9542bf2d3c7b5276b49062e1c))
* **packets:** point PositionTooShort at the end of a cut-off packet ([40c4ed9](https://github.com/APRS-NET/APRS.NET/commit/40c4ed998f7223cdeb1fb872266d4d1412682c41))
* **packets:** read objects whose names are not padded to 9 ([407a7e6](https://github.com/APRS-NET/APRS.NET/commit/407a7e6bc8c1341e1f8123db1dd7c765869a0101))
* **packets:** read positionless weather with an unreadable timestamp ([1c91eda](https://github.com/APRS-NET/APRS.NET/commit/1c91edac6df9a11f416633ecd5f1e4a44641d102))


### Performance Improvements

* **filters:** work out packet facts on demand and match without LINQ ([28df04c](https://github.com/APRS-NET/APRS.NET/commit/28df04cdd48609eeb0d9aef9fde9cf22adc8eea8))


### Documentation

* describe the 0.2.0 milestone in the README ([6a1bed6](https://github.com/APRS-NET/APRS.NET/commit/6a1bed6d65de29d8625179a6c2ce21b6d56f9a70))

## [0.1.0-alpha.17](https://github.com/APRS-NET/APRS.NET/compare/v0.1.0-alpha.16...v0.1.0-alpha.17) (2026-10-15)


### Features

* **aprs-is:** reconnect automatically with AprsIsConnection ([63263c3](https://github.com/APRS-NET/APRS.NET/commit/63263c3e1d87f0f766908be8efd21242e128cf4f))

## [0.1.0-alpha.16](https://github.com/APRS-NET/APRS.NET/compare/v0.1.0-alpha.15...v0.1.0-alpha.16) (2026-10-15)


### Features

* **aprs-is:** send packets and messages on a verified login ([ac4546c](https://github.com/APRS-NET/APRS.NET/commit/ac4546c72ce23789183f6247adccbacbc0db33f3))

## [0.1.0-alpha.15](https://github.com/APRS-NET/APRS.NET/compare/v0.1.0-alpha.14...v0.1.0-alpha.15) (2026-10-14)


### Features

* give validation errors stable codes for translation ([6c373e6](https://github.com/APRS-NET/APRS.NET/commit/6c373e6c5bbd78bf04746698b4cfd0ad1216513b))

## [0.1.0-alpha.14](https://github.com/APRS-NET/APRS.NET/compare/v0.1.0-alpha.13...v0.1.0-alpha.14) (2026-10-14)


### Features

* **packets:** build Mic-E reports with AprsPacket.Create ([f6ec72b](https://github.com/APRS-NET/APRS.NET/commit/f6ec72b665aaac36f9fd1bac79b8fae14a84c889))
* **packets:** build objects and items with AprsPacket.Create ([1b04515](https://github.com/APRS-NET/APRS.NET/commit/1b045156a76bf0a1bc1259eaa38e388bc2d5a8b3))

## [0.1.0-alpha.13](https://github.com/APRS-NET/APRS.NET/compare/v0.1.0-alpha.12...v0.1.0-alpha.13) (2026-10-14)


### Features

* **packets:** send as one station with PacketAddress ([b82f71b](https://github.com/APRS-NET/APRS.NET/commit/b82f71b6a268d28de23b4d5acc9a7b450c818084))

## [0.1.0-alpha.12](https://github.com/APRS-NET/APRS.NET/compare/v0.1.0-alpha.11...v0.1.0-alpha.12) (2026-10-14)


### Features

* **aprs-is:** list the aprs2.net rotates as server presets ([74d78a6](https://github.com/APRS-NET/APRS.NET/commit/74d78a618972507db04124333c0bfbf34cc2592e))
* **packets:** build position reports with AprsPacket.Create ([5c068c3](https://github.com/APRS-NET/APRS.NET/commit/5c068c3d45c92ec7c3e54ba79e3365a1b65bc297))
* **packets:** build status reports and messages with AprsPacket.Create ([513ce70](https://github.com/APRS-NET/APRS.NET/commit/513ce70544435334ee31aa938d468e4cfcf50371))
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
