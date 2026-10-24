// swift-tools-version:5.9
import PackageDescription

let package = Package(
    name: "Lemon",
    platforms: [.macOS(.v14)],
    dependencies: [
        // Same engine as the Windows build's BiRefNet background remover.
        .package(url: "https://github.com/microsoft/onnxruntime-swift-package-manager", exact: "1.24.2"),
    ],
    targets: [.executableTarget(
        name: "Lemon",
        dependencies: [.product(name: "onnxruntime", package: "onnxruntime-swift-package-manager")],
        path: "Sources/Lemon"),
        .testTarget(name: "LemonTests", dependencies: ["Lemon"], path: "Tests/LemonTests")]
)
