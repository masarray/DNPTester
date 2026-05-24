# Third-Party Notices

This application source code is distributed under the GNU General Public License version 3. Third-party dependencies, runtimes, SDKs, and tooling remain governed by their own license terms.

## DNP3 master stack

The master tester now uses its own C# DNP3 master implementation for active
master communication. It is not linked against or distributed with the Step
Function I/O `dnp3` runtime in the master application.

The protocol design was informed by public DNP3 materials, IEEE/DNP-UG protocol
concepts, and permissive public references such as OpenDNP3. Do not copy
proprietary DNP3 implementation code into this repository.

The previous third-party slave simulator and local vendor reference tree have
been removed from this repository.

## Other Dependencies

This project uses System.IO.Ports for serial transport. Its license files are provided by its NuGet package and should be preserved in distribution review.

## Installer Compliance Note

The Inno Setup build packages the application output. Before public distribution, rebuild from the current GPL-3 source tree and review all files in the release output so stale artifacts are not shipped.
