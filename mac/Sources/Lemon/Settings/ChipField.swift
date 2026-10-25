import SwiftUI
import AppKit

/// A placeholder drawn as a colored capsule with its name, one character wide in the text.
final class ChipAttachment: NSTextAttachment {
    let token: Token
    init(_ token: Token, font: NSFont) {
        self.token = token
        super.init(data: nil, ofType: nil)
        let label = NSAttributedString(string: token.label, attributes: [
            .font: NSFont.systemFont(ofSize: font.pointSize - 1, weight: .semibold), .foregroundColor: NSColor.white])
        let size = NSSize(width: ceil(label.size().width) + 14, height: ceil(font.ascender - font.descender) + 3)
        image = NSImage(size: size, flipped: false) { r in
            token.color.setFill()
            NSBezierPath(roundedRect: r.insetBy(dx: 1, dy: 0.5), xRadius: r.height / 2, yRadius: r.height / 2).fill()
            label.draw(at: NSPoint(x: 7, y: (r.height - label.size().height) / 2))
            return true
        }
        bounds = CGRect(x: 0, y: font.descender - 1.5, width: size.width, height: size.height)
    }
    @available(*, unavailable) required init?(coder: NSCoder) { fatalError() }
}

/// A command line with each placeholder ({folder}, {link}…) as a chip. Inverse of `chipText`.
func chipString(_ line: String, font: NSFont) -> NSAttributedString {
    let out = NSMutableAttributedString(), plain: [NSAttributedString.Key: Any] = [.font: font, .foregroundColor: NSColor.textColor]
    for piece in tokenize(line) {
        out.append(piece.token.map { NSAttributedString(attachment: ChipAttachment($0, font: font)) } ?? NSAttributedString(string: piece.text, attributes: plain))
    }
    return out
}

/// The command line in an editor's text: chips back to their placeholders.
func chipText(_ s: NSAttributedString) -> String {
    var out = ""
    s.enumerateAttribute(.attachment, in: NSRange(location: 0, length: s.length)) { v, r, _ in
        if let chip = v as? ChipAttachment { out += chip.token.text }
        else if v == nil { out += (s.string as NSString).substring(with: r) }
    }
    return out
}

/// Lets buttons outside the field put a chip where the cursor is.
final class ChipFieldControl {
    fileprivate weak var view: NSTextView?
    static let font = NSFont.monospacedSystemFont(ofSize: 12, weight: .regular)
    func insert(_ t: Token) {
        guard let v = view else { return }
        v.window?.makeFirstResponder(v)
        v.insertText(NSAttributedString(attachment: ChipAttachment(t, font: Self.font)), replacementRange: v.selectedRange())
    }
}

/// Copies and pastes the command as plain text, so chips travel as their placeholders and come back as chips.
private final class ChipTextView: NSTextView {
    override func copy(_ sender: Any?) {
        guard let s = textStorage, selectedRange().length > 0 else { return }
        NSPasteboard.general.clearContents()
        NSPasteboard.general.setString(chipText(s.attributedSubstring(from: selectedRange())), forType: .string)
    }
    override func cut(_ sender: Any?) { copy(sender); delete(sender) }
    override func paste(_ sender: Any?) { pasteAsPlainText(sender) }
}

/// A text box for a command line where placeholders show as chips. Typing or pasting {folder} turns it into one.
struct ChipField: NSViewRepresentable {
    @Binding var text: String
    let control: ChipFieldControl

    func makeCoordinator() -> Coordinator { Coordinator(self) }

    func makeNSView(context: Context) -> NSScrollView {
        let tv = ChipTextView()
        tv.isRichText = true; tv.importsGraphics = false; tv.allowsUndo = true
        // A shell line: curly quotes or an en dash would break it.
        tv.isAutomaticQuoteSubstitutionEnabled = false; tv.isAutomaticDashSubstitutionEnabled = false
        tv.isAutomaticTextReplacementEnabled = false; tv.isContinuousSpellCheckingEnabled = false; tv.isAutomaticSpellingCorrectionEnabled = false
        tv.font = ChipFieldControl.font; tv.typingAttributes = [.font: ChipFieldControl.font, .foregroundColor: NSColor.textColor]
        tv.drawsBackground = false; tv.textContainerInset = NSSize(width: 2, height: 5)
        tv.isVerticallyResizable = true; tv.autoresizingMask = .width; tv.textContainer?.widthTracksTextView = true
        tv.textStorage?.setAttributedString(chipString(text, font: ChipFieldControl.font))
        tv.delegate = context.coordinator
        control.view = tv
        let scroll = NSScrollView()
        scroll.documentView = tv; scroll.hasVerticalScroller = true; scroll.drawsBackground = false
        return scroll
    }

    func updateNSView(_ scroll: NSScrollView, context: Context) {
        context.coordinator.parent = self
        // SwiftUI makes a new editor struct (and control) on redraws: keep the buttons pointed at this text view.
        control.view = scroll.documentView as? NSTextView
        guard let tv = scroll.documentView as? NSTextView, let s = tv.textStorage, chipText(s) != text else { return }
        s.setAttributedString(chipString(text, font: ChipFieldControl.font))
    }

    final class Coordinator: NSObject, NSTextViewDelegate {
        var parent: ChipField
        init(_ parent: ChipField) { self.parent = parent }
        func textDidChange(_ n: Notification) {
            guard let tv = n.object as? NSTextView, let s = tv.textStorage else { return }
            let text = chipText(s), rebuilt = chipString(text, font: ChipFieldControl.font)
            // A placeholder typed or pasted as text: show it as a chip, with the cursor after the same text as before.
            if rebuilt.length != s.length {
                let before = chipText(s.attributedSubstring(from: NSRange(location: 0, length: tv.selectedRange().location)))
                // Through the text view, not the storage, so Undo knows about it.
                tv.insertText(rebuilt, replacementRange: NSRange(location: 0, length: s.length))
                tv.setSelectedRange(NSRange(location: chipString(before, font: ChipFieldControl.font).length, length: 0))
            }
            tv.typingAttributes = [.font: ChipFieldControl.font, .foregroundColor: NSColor.textColor]
            parent.text = text
        }
    }
}
