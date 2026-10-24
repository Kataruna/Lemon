import Foundation

/// Looks on GitHub for a newer Mac release of Lemon. The Mac and Windows apps are released separately, tagged
/// "mac-v…" and "windows-v…", so GitHub's single "latest" release could be the Windows one; this reads the list instead.
@MainActor final class Updates: ObservableObject {
    static let shared = Updates()
    @Published var available: (version: String, url: URL)?
    /// From Info.plist, so nil under `swift run`: a development build doesn't check.
    nonisolated static var current: String? { Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String }

    func check() async {
        guard PrefStore.shared.p.checkUpdates ?? true, let current = Self.current else { return }
        var r = URLRequest(url: URL(string: "https://api.github.com/repos/Kataruna/Lemon/releases?per_page=30")!)
        r.setValue("Lemon/\(current)", forHTTPHeaderField: "User-Agent")   // GitHub's API turns away requests without one
        r.setValue("application/vnd.github+json", forHTTPHeaderField: "Accept")
        // Offline or a GitHub error just means no notice.
        guard let (data, response) = try? await URLSession.shared.data(for: r), (response as? HTTPURLResponse)?.statusCode == 200 else { return }
        available = newestRelease(data, current: current)
    }
}

/// From GitHub's release list: the newest published (not draft or pre-release) Mac release newer than `current`.
func newestRelease(_ json: Data, current: String) -> (version: String, url: URL)? {
    struct Release: Decodable { let tag_name: String, html_url: String, draft: Bool, prerelease: Bool }
    guard let list = try? JSONDecoder().decode([Release].self, from: json), let mine = semver(current) else { return nil }
    var best: (v: [Int], version: String, url: URL)?
    for r in list where !r.draft && !r.prerelease && r.tag_name.lowercased().hasPrefix("mac-v") {
        let version = String(r.tag_name.dropFirst(5))
        guard let v = semver(version), let url = URL(string: r.html_url), mine.lexicographicallyPrecedes(v),
              best.map({ $0.v.lexicographicallyPrecedes(v) }) ?? true else { continue }
        best = (v, version, url)
    }
    return best.map { ($0.version, $0.url) }
}

/// Major.minor.patch only, missing parts as 0: "0.3" and "0.3.0" are the same version.
func semver(_ s: String) -> [Int]? {
    let parts = s.split(separator: ".").prefix(3).map { Int($0) }
    guard !parts.isEmpty, !parts.contains(nil) else { return nil }
    return parts.compactMap { $0 } + Array(repeating: 0, count: 3 - parts.count)
}
