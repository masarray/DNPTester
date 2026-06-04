# Third-Party Notices

This application source code is distributed under the Apache License, Version 2.0. Third-party dependencies, runtimes, SDKs, and tooling remain governed by their own license terms.

## DNP3 master stack

The master tester now uses its own C# DNP3 master implementation for active
master communication. It is not linked against or distributed with the Step
Function I/O `dnp3` runtime in the master application.

The protocol design was informed by public DNP3 materials, IEEE/DNP-UG protocol
concepts, and permissive public references such as OpenDNP3. Do not copy
proprietary DNP3 implementation code into this repository.

## Other Dependencies

This project uses System.IO.Ports for serial transport. Its license files are provided by its NuGet package and should be preserved in distribution review.

## Installer Compliance Note

The Inno Setup build packages the application output. Before public distribution, rebuild from the current Apache-2.0 source tree and review all files in the release output so stale artifacts are not shipped.
