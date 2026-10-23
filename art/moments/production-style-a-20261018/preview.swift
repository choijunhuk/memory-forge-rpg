import AppKit
import Foundation

// Inspection contact sheet only; never changes production PNGs or their alpha.
let args = Array(CommandLine.arguments.dropFirst())
guard args.count >= 2 else { fatalError("preview.swift output.png input.png ...") }
let output = args[0]
let paths = Array(args.dropFirst())
let cellW = 256, cellH = 400
let sheet = NSImage(size: NSSize(width: cellW * paths.count, height: cellH * 2))
sheet.lockFocus()
for (i, path) in paths.enumerated() {
    guard let image = NSImage(contentsOfFile: path) else { fatalError(path) }
    for row in 0..<2 {
        let rect = NSRect(x: i * cellW, y: row * cellH, width: cellW, height: cellH)
        (row == 0 ? NSColor.white : NSColor(calibratedWhite: 0.14, alpha: 1)).setFill()
        rect.fill()
        let scale = min(CGFloat(cellW - 12) / image.size.width, CGFloat(cellH - 28) / image.size.height)
        let size = NSSize(width: image.size.width * scale, height: image.size.height * scale)
        image.draw(in: NSRect(x: rect.midX - size.width / 2, y: rect.minY + 22, width: size.width, height: size.height), from: .zero, operation: .sourceOver, fraction: 1)
        let title = URL(fileURLWithPath: path).lastPathComponent
        (title as NSString).draw(at: NSPoint(x: rect.minX + 4, y: rect.minY + 4), withAttributes: [.font: NSFont.systemFont(ofSize: 8), .foregroundColor: row == 0 ? NSColor.black : NSColor.white])
    }
}
sheet.unlockFocus()
guard let tiff = sheet.tiffRepresentation, let bitmap = NSBitmapImageRep(data: tiff), let png = bitmap.representation(using: .png, properties: [:]) else { fatalError("render") }
try png.write(to: URL(fileURLWithPath: output))
