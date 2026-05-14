# Third-Party Notices

This application can publish its own source code under the repository license, but that does not relicense third-party dependencies.

## Step Function I/O dnp3

- Package: `dnp3`
- Version: `1.7.0-RC1`
- Project: https://stepfunc.io/products/libraries/dnp3/
- Repository metadata: https://github.com/stepfunc/dnp3.git
- Package license file: `LICENSE.txt` inside the NuGet package

The Step Function I/O DNP3 package is a proprietary third-party dependency with its own license. The package license grants non-commercial and non-production rights unless a separate commercial/production license has been obtained from Step Function I/O.

Do not describe the DNP3 library itself as MIT/open-source. Do not distribute a production installer that includes the DNP3 library unless the applicable Step Function I/O license permits that distribution.

## Other Dependencies

This project also uses third-party packages such as Microsoft WebView2, QuestPDF, and System.IO.Ports. Their license files are provided by their NuGet packages and should be preserved in distribution review.

## Installer Compliance Note

The Inno Setup build packages the application output. Before public or commercial distribution, review all files in the release output and confirm that each dependency license allows the intended use and redistribution.
