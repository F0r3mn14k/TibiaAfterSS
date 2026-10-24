// swift-tools-version: 5.9
import PackageDescription

let package = Package(
    name: "TibiaAfterSSMac",
    platforms: [.macOS(.v13)],
    products: [.executable(name: "TibiaAfterSSMac", targets: ["TibiaAfterSSMac"])],
    targets: [.executableTarget(name: "TibiaAfterSSMac")]
)

