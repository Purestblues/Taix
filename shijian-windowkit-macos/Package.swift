// swift-tools-version:5.9
import PackageDescription

let package = Package(
    name: "ShiJianWindowKit",
    platforms: [.macOS(.v12)],
    products: [
        .library(name: "ShiJianWindowKit", type: .dynamic, targets: ["ShiJianWindowKit"])
    ],
    targets: [
        .target(
            name: "ShiJianWindowKit",
            linkerSettings: [
                .linkedFramework("AppKit")
            ]
        )
    ]
)
