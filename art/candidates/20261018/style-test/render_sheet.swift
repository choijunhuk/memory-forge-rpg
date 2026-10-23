import AppKit
import ImageIO

// Deterministic presentation only: existing PNG pixels are scaled/cropped, never repainted.
let root = URL(fileURLWithPath: CommandLine.arguments.count > 1 ? CommandLine.arguments[1] : FileManager.default.currentDirectoryPath)
let project = URL(fileURLWithPath: "/Users/choi/Documents/Memory_forge")
let width = 3360, height = 3660
let columns = ["기존 / CURRENT", "A  선명한 셀", "B  절제된 반셀", "C  그래픽 페인터리"]
let descriptions = ["기존 로비 원본 · 비교 기준", "선명한 눈선 · 큰 셀 명암 · 무광 피부", "가는 색선 · 좁은 전이 · 재질 구분", "각진 붓면 · 선택적 질감 · 차분한 색"]
let rows = [
    ("SERA / 세라 · 24세", "sera", "art/moments/sera/images/char_sera_lobby_scope_v02.png", CGRect(x: 390, y: 14, width: 310, height: 310)),
    ("HARIN / 하린 · 26세", "harin", "art/moments/harin/images/char_harin_stand_neutral_v01.png", CGRect(x: 350, y: 0, width: 310, height: 310))
]

func load(_ url: URL) -> CGImage {
    guard let source = CGImageSourceCreateWithURL(url as CFURL, nil), let image = CGImageSourceCreateImageAtIndex(source, 0, nil) else { fatalError("Cannot decode: \(url.path)") }
    return image
}
let bitmap = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: width, pixelsHigh: height, bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0)!
let graphics = NSGraphicsContext(bitmapImageRep: bitmap)!
NSGraphicsContext.saveGraphicsState()
NSGraphicsContext.current = graphics
graphics.imageInterpolation = .high
NSColor(calibratedRed: 0.96, green: 0.955, blue: 0.94, alpha: 1).setFill()
NSRect(x: 0, y: 0, width: width, height: height).fill()

func rect(_ x: CGFloat, _ y: CGFloat, _ w: CGFloat, _ h: CGFloat) -> NSRect { NSRect(x: x, y: CGFloat(height) - y - h, width: w, height: h) }
func label(_ string: String, _ x: CGFloat, _ y: CGFloat, _ w: CGFloat, _ size: CGFloat, _ bold: Bool = false, _ color: NSColor = .darkGray) {
    let font = NSFont.systemFont(ofSize: size, weight: bold ? .bold : .regular)
    (string as NSString).draw(in: rect(x, y, w, size * 1.7), withAttributes: [.font: font, .foregroundColor: color])
}
func draw(_ image: CGImage, _ x: CGFloat, _ y: CGFloat, _ w: CGFloat, _ h: CGFloat) {
    NSImage(cgImage: image, size: NSSize(width: image.width, height: image.height)).draw(in: rect(x, y, w, h), from: .zero, operation: .sourceOver, fraction: 1)
}

label("MEMORY FORGE", 60, 36, 1900, 46, true)
label("그림체 선택 비교  /  2 characters × 3 directions", 60, 104, 2600, 32, true)
label("동일 나이·기본복·주요 소품 유지  ·  스타일 선택용 초안  ·  2026.10.18", 60, 157, 2800, 24)
let columnWidth: CGFloat = 795, gap: CGFloat = 20, left: CGFloat = 60
for col in 0..<4 {
    let x = left + CGFloat(col) * (columnWidth + gap)
    label(columns[col], x, 233, columnWidth, 31, true)
    label(descriptions[col], x, 280, columnWidth, 21)
}

for (row, data) in rows.enumerated() {
    let y: CGFloat = 350 + CGFloat(row) * 1570
    label(data.0, 60, y, 2700, 28, true)
    for col in 0..<4 {
        let url = col == 0 ? project.appendingPathComponent(data.2) : root.appendingPathComponent("individuals/\(["a", "b", "c"][col - 1])_\(data.1)_v03.png")
        let image = load(url)
        let x = left + CGFloat(col) * (columnWidth + gap)
        NSColor(calibratedRed: 0.948, green: 0.941, blue: 0.922, alpha: 1).setFill()
        rect(x, y + 52, columnWidth, 1120).fill()
        let scale = min((columnWidth - 16) / CGFloat(image.width), 1120 / CGFloat(image.height))
        let iw = CGFloat(image.width) * scale, ih = CGFloat(image.height) * scale
        draw(image, x + (columnWidth - iw) / 2, y + 52 + (1120 - ih) / 2, iw, ih)
        guard let crop = image.cropping(to: data.3) else { fatalError("Invalid face crop") }
        NSColor(calibratedWhite: 0.96, alpha: 1).setFill()
        rect(x, y + 1195, columnWidth, 310).fill()
        draw(crop, x + (columnWidth - 310) / 2, y + 1195, 310, 310)
        label("얼굴 1:1 crop · 동일 310×310 px 영역", x + 12, y + 1514, columnWidth - 24, 18)
    }
}
label("추천 A  ·  셀 명암과 간결한 얼굴 기호가 가장 또렷함.  B는 보수적 전환, C는 붓맛을 우선한 대안.", 60, 3524, 3240, 25, true)
label("외부 공식 이미지는 이 비교판에 사용하지 않았습니다. 기존/시안 PNG의 배치와 얼굴 크롭만 수행했습니다.", 60, 3577, 3240, 21)
NSGraphicsContext.restoreGraphicsState()
guard let png = bitmap.representation(using: .png, properties: [:]) else { fatalError("PNG encoding failed") }
try png.write(to: root.appendingPathComponent("COMPARISON.png"))
print("COMPARISON.png \(width)x\(height)")
