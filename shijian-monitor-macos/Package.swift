// swift-tools-version:5.9
import PackageDescription

let package = Package(
    name: "ShiJianMonitor",
    platforms: [.macOS(.v12)],
    products: [
        .executable(name: "shijian-monitor", targets: ["ShiJianMonitor"])
    ],
    targets: [
        .executableTarget(
            name: "ShiJianMonitor",
            swiftSettings: [
                .unsafeFlags(["-parse-as-library"])
            ],
            linkerSettings: [
                .linkedFramework("AppKit"),
                .linkedFramework("CoreAudio"),
                .linkedFramework("IOKit"),
                .linkedFramework("CoreFoundation")
            ]
        )
    ]
)
