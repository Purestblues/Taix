// swift-tools-version:5.9
import PackageDescription

let package = Package(
    name: "ShiJianNotify",
    platforms: [.macOS(.v12)],
    products: [
        .executable(name: "ShiJianNotify", targets: ["ShiJianNotify"])
    ],
    targets: [
        .executableTarget(
            name: "ShiJianNotify",
            swiftSettings: [
                .unsafeFlags(["-parse-as-library"])
            ],
            linkerSettings: [
                .linkedFramework("UserNotifications")
            ]
        )
    ]
)
