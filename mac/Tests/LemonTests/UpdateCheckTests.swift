import XCTest
@testable import Lemon

final class UpdateCheckTests: XCTestCase {
    private func release(_ tag: String, draft: Bool = false, pre: Bool = false) -> String {
        #"{"tag_name":"\#(tag)","html_url":"https://github.com/Kataruna/Lemon/releases/tag/\#(tag)","draft":\#(draft),"prerelease":\#(pre)}"#
    }
    private func newest(_ tags: [String], current: String) -> String? {
        newestRelease(Data("[\(tags.joined(separator: ","))]".utf8), current: current)?.version
    }

    func testPicksNewestPublishedMacRelease() {
        let list = [release("windows-v9.0.0"), release("mac-v0.5.0", draft: true), release("mac-v0.4.0", pre: true),
                    release("mac-v0.3.1"), release("mac-v0.10.0"), release("mac-vbad")]
        XCTAssertEqual(newest(list, current: "0.3.0"), "0.10.0")   // numeric, not text, comparison
    }

    func testNothingWhenUpToDate() {
        XCTAssertNil(newest([release("mac-v0.3.0"), release("mac-v0.2.0")], current: "0.3.0"))
        XCTAssertNil(newest([release("mac-v0.3")], current: "0.3.0"))   // 0.3 is 0.3.0
        XCTAssertNil(newestRelease(Data("not json".utf8), current: "0.3.0"))
    }
}
