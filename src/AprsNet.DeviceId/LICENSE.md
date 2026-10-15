# AprsNet.DeviceId licence

This package has two parts under two licences.

## The code: MIT

Everything except the device table is licensed under the MIT License:

```
MIT License

Copyright (c) 2026 APRS.NET

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## The device table: CC BY-SA 2.0

The device table compiled into this package (`AprsDevices.Generated.cs` in the source) is adapted from the
[aprs-deviceid](https://github.com/aprsorg/aprs-deviceid) list, maintained by Heikki Hannikainen, OH7LZB, and
contributors, and licensed under the
[Creative Commons Attribution-ShareAlike 2.0 Generic](https://creativecommons.org/licenses/by-sa/2.0/) licence.
The adaptation converts the published list (`tocalls.pretty.json`) to C# and leaves out its contact addresses; the
source commit is in `AprsDevices.ListCommit`. The device table is licensed under the same CC BY-SA 2.0 licence.
