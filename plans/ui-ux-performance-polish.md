# Kế hoạch chỉnh giao diện chuẩn UX/UI và chống lag — AutoVerify

> Phạm vi: frontend `AutoVerify_System/src/web` (React 19 + Vite + `echarts-for-react`, CSS thuần trong `App.css`).
> Không đụng backend và không đổi API.
> Ngày lập: 2026-10-21 · Rà soát lại với code: 2026-10-21 · Trạng thái: **Chưa bắt đầu**
> Tiếp nối `plans/ui-refactor-horizontal-tabs.md`. Code hiện tại đã gom thành 4 phân hệ, nhưng vẫn dùng **sidebar dọc + thanh tab con ngang**, chưa chuyển sang header ngang như plan đó đề xuất.
> Plan này lo phần **tốc độ** và **độ chuẩn UX/UI** còn thiếu.

## Tiến độ

Ký hiệu: ✅ xong · ⏳ đang làm · ⬜ chưa làm

| Giai đoạn | Nội dung | Ước lượng | Trạng thái |
|---|---|---|---|
| A | Đo hiện trạng | 0.5 ngày | ✅ (đo kích thước build; chưa đo Lighthouse/INP) |
| B | Giảm dung lượng tải: chia nhỏ bundle, thu gọn ECharts | 1 ngày | ✅ 2026-10-21 |
| C | Giữ tab và dữ liệu khi chuyển, bỏ tải lại thừa | 1 ngày | ⏳ C1, C2, C5 xong; C3, C4, C6 chưa |
| D | Chặn render thừa ở các tab lớn | 1.5 ngày | ✅ D1–D5, D7 xong (đã đo từng tab nặng); D6 chưa (không cần, xem nhật ký); D8 đã thử và loại |
| E | Ảnh, bảng, polling | 1 ngày | ⏳ E3, E4 xong; E1, E6 một phần; E2, E5, E7 chưa |
| F | Nền tảng giao diện chuẩn (design tokens, component dùng chung) | 1.5 ngày | ⏳ F2, F3, F6, F9 xong; F1, F4, F8 một phần; F5, F7 chưa |
| G | Chuẩn UX: trạng thái, phản hồi, bàn phím, truy cập | 1.5 ngày | ⏳ G1, G4, G5, G9, G10, G11 xong; G2, G3, G7 một phần; G6, G8 xong |
| H | Kiểm tra và phát hành | 0.5 ngày | ✅ 2026-10-22 (frontend; backend chưa publish lại, xem nhật ký) |

Thứ tự khuyến nghị: **A → B → C → D → E**, rồi F → G. B và C là hai bước làm app nhanh hơn rõ nhất.


## Nhật ký thực hiện

**2026-10-21 · Giai đoạn A–B (xong)**
- Baseline: `index-*.js` 1.763 KB / **523 KB gzip** (một file).
- Sau B: tải đầu (đăng nhập) = `index` 17 KB + `react-vendor` 60 KB = **~77 KB gzip**. Mỗi tab là một chunk riêng (1–15 KB gzip). ECharts nằm ở chunk riêng **205 KB gzip** (trước đó 342 KB), chỉ tải khi biểu đồ đầu tiên cuộn vào khung nhìn.
- `echarts-for-react` dùng bản ESM (`esm/core`), không dùng `lib/core` (bản CJS bị lỗi interop trong bản build production: React error #130). `containLabel` không cần feature riêng ở ECharts 5.6.
- Đã chạy thử trên Chrome thật (Playwright, API giả): Dashboard vẽ đủ 2 biểu đồ (cột + đường + bar), không có lỗi console. **Chưa kiểm tra bằng mắt** các biểu đồ scatter / heatmap / pie / markLine / visualMap trong tab Analytics, Overkill Analysis, OP Reconciliation (đã đăng ký trong `lib/echarts.ts`, cần mở từng biểu đồ với dữ liệu thật).
- B7: `ImportTab` không còn được import và render trong `App.tsx` (file giữ nguyên, `'import'` còn trong `TabId`).
- B8 (backend): thêm nén Brotli/Gzip (`UseResponseCompression`, cả file tĩnh lẫn JSON của API) và cache (`/assets/*` immutable 1 năm, `index.html` no-cache) trong `Program.cs`. `dotnet build` sạch; **chưa chạy server thật** (cần MariaDB), nên chưa xác nhận header `Content-Encoding` ngoài thực tế.

**2026-10-21 · Giai đoạn C, E (một phần)**
- C1/C2: tab đã mở được giữ trong bộ nhớ (ẩn bằng `hidden`), tối đa 5; `TabPane` bọc `memo`; tab ẩn nhận `active=false` qua `TabActiveContext`.
- C5: URL hash `#/operations/leakage-ops`. Kiểm tra: link sâu vào đúng tab, nút Back/Forward, tải lại giữ vị trí, chữ gõ trong ô lọc còn nguyên sau khi đổi tab, vị trí cuộn được nhớ theo tab.
- E3/E4: hook `usePolling` (chờ lần trước xong mới hẹn lần sau, dừng khi tab hoặc trình duyệt bị ẩn). Đã thay `setInterval` ở `LeakageOpsTab` (2.5s khi đang chạy, 15s khi rảnh), `AiVerificationTab`, `ProductsTab`, `PipelineHub` (2 timer), `DefectPreviewGrid`. Kiểm tra: không có request nào khi chỉ có tab khác đang hiện.
- Phát hiện khi thử: **một tab bị lỗi khi render làm trắng cả ứng dụng** (không có error boundary). Đã thêm việc G11.

**2026-10-21 · Giai đoạn F, G, E (một phần)**
- F2: toàn bộ chữ dưới 12px trong `App.css` (khoảng 158 chỗ) và style inline dạng chuỗi (168 chỗ) và HTML tooltip của biểu đồ đã nâng lên 12px. Nhãn trục bên trong option ECharts (dạng số, ví dụ `fontSize: 10`, khoảng 100 chỗ) **giữ nguyên**. Dòng "VERIFICATION SYSTEM" ở logo đã chỉnh `letter-spacing` để không xuống hàng. **Chưa chụp kiểm từng tab**: chữ lớn hơn có thể làm một số ô dày đặc bị chật (mới xem Dashboard); bản gốc `App.css` lưu ở `scratchpad\App.css.bak`, và mọi thay đổi đều nằm trong git nên có thể hoàn tác.
- E6: `transition: all` (12 chỗ) đã thay bằng danh sách thuộc tính. Animation lặp vô hạn và `backdrop-filter` chưa động tới.
- E1: `LazyImage` (lazy-load, decode async, báo lỗi và nút Retry trong ô) đang dùng ở lưới thumbnail Overkill và ảnh trong `DefectFeatureInspector`. Còn 4 chỗ `<img>` khác (`ImageViewer`, phóng to, `LeakageOpsTab`, `BatchPipelinesModal`).
- F9: bản production không còn điền sẵn tài khoản, không còn dòng "Temporary MVP credentials", có nút Show/Hide mật khẩu. Bản `npm run dev` vẫn điền sẵn.
- G2 (một phần): thông báo lỗi **không tự biến mất**, có nút đóng, `role="alert"`, dính trên đầu khi cuộn; thông báo thành công/info vẫn tự ẩn sau 6s. Chưa xếp chồng nhiều thông báo.
- G5/F8 (một phần): thanh tab con theo WAI-ARIA (mũi tên, Home/End, `tabindex` luân phiên, `role="tabpanel"`), cuộn ngang thay vì xuống dòng, tab đang chọn tự cuộn vào khung nhìn. Chưa thu gọn sidebar.
- G11: `ErrorBoundary` cho từng tab. Đã kiểm tra: tab lỗi chỉ hiện khung lỗi + nút Try again, thanh điều hướng vẫn dùng được.

**2026-10-21 · Giai đoạn D (một phần)**
- **Đo bằng benchmark** (Chrome, CPU throttle 4×, tab Products, bảng 100 dòng, gõ 30 ký tự vào ô Search, 3 lần chạy): thời gian script mỗi lần gõ **~100 ms → ~20 ms** (tổng 2.900–3.900 ms → 500–840 ms); số lần bảng bị vẽ lại **30 → 0** (đếm bằng số lần gọi bộ định dạng ngày). Thời gian đo dao động mạnh giữa các lần chạy (±50%), nên số lần vẽ lại đáng tin hơn thời gian.
- D1: `DebouncedInput` (`ui.tsx`) giữ chữ đang gõ trong state của chính ô, chỉ báo lên trang khi ngừng gõ (250–350 ms), nhấn Enter hoặc rời ô. Áp dụng cho 6 ô tìm kiếm: Products, Leakage Operations, Classifier Rules, OP Reconciliation (mapping), Connections & Sources, Pipeline Hub. Đã kiểm tra: Enter và nút Apply gửi đúng từ khoá ngay, Reset xoá ô, gõ mà không bấm gì thì không gọi API. **Phát hiện kèm theo:** ở Leakage Operations, ô tìm kiếm trước đây gọi API sau **mỗi ký tự** (do `loadItems` phụ thuộc `searchQuery`); giờ chỉ gọi sau khi ngừng gõ.
- D2/D3: `DataTable` bọc `memo`; bảng Products dựng `rows` bằng `useMemo` và `columns` là hằng số, nên đổi ô lọc không vẽ lại 25–100 dòng. **Chưa làm cho các tab khác** (Overkill Analysis, Queries, OP Reconciliation, Sources, Storage…): `DataTable` chỉ bỏ qua render khi nơi gọi cũng giữ `rows` ổn định.
- D4: thanh trượt chiều cao biểu đồ ở Overkill Analysis dùng `useDeferredValue` (nhãn chạy theo ngay, biểu đồ đổi kích thước sau).
- D5: biểu đồ có trên 3.000 điểm tự tắt animation (`ReactECharts.tsx`). Chưa bật `large` / `sampling` vì chế độ `large` của ECharts không hỗ trợ style riêng từng điểm.
- D7: `RuleOptimizerModal`, `OpReconciliationTab` (nhúng ở 2 nơi) và `DefectFeatureAnalyticsDashboard` tải theo yêu cầu. `BatchPipelinesModal` / `BatchConnectionsModal` giữ nguyên vì luôn được render sẵn.
- D8 **thử và loại bỏ**: bật React Compiler thì 69/82 component bị bỏ qua vì code dùng `try { } finally { }` (compiler chưa hỗ trợ); lợi ích đo được nhỏ và nhiễu, nên đã gỡ, không thêm phụ thuộc. Muốn dùng lại cần viết lại các `try/finally` trong handler.
- Phát hiện: `components/DefectPreviewGrid.tsx` và nhóm `components/analytics/*` (SPC, PCA, KMeans, GMM, …) **không được import ở đâu**: đó là mã chết, không có trong bundle. Có thể xoá sau khi bạn xác nhận không dùng.

**2026-10-22 · Rà soát lại, responsive, token, truy cập**
- Rà soát: `tsc`, `oxlint` (chỉ còn cảnh báo có từ trước), build đều sạch.
- **G9 Responsive**: chụp 15 tab ở 1440px và 390px (API giả tự sinh từ kiểu trả về trong `api.ts`). Phát hiện và sửa **lỗi tràn ngang có từ trước ở 390px**: cột lưới `1fr` (và `.tab-stack`) có chiều rộng tối thiểu bằng nội dung nên bị bảng/thanh điều hướng kéo rộng trang (đã đổi sang `minmax(0, 1fr)`); thêm `overflow-x` cho `.table-responsive`, `code` tự xuống dòng, `section-heading` cho phép xuống dòng, hàng nút chuyển view ở Queries xuống dòng. Kết quả: **0 tab tràn ngang** ở cả hai kích thước. Chữ 12px không làm vỡ bố cục ở các tab đã xem.
- **F1 Design tokens (một phần)**: khối `:root` định nghĩa 45 màu (slate, blue, red, amber, green…); **830 chỗ** trong `App.css` đã đổi sang `var(--…)`. So sánh điểm ảnh 30 ảnh chụp trước/sau: **0 điểm ảnh khác**. Còn khoảng 90 màu hiếm gặp (và toàn bộ style inline trong TSX) chưa quy đổi; chưa có token cho khoảng cách, bo góc, bóng.
- **G10/G6 Kiểm tra truy cập bằng axe-core** (13 tab có dữ liệu mẫu): từ 6 loại lỗi/khoảng 175 vị trí xuống **0 lỗi**. Đã sửa: tương phản chữ (tiêu đề cột, eyebrow, phân trang, sidebar, `.text-muted`, các badge màu) lên chuẩn AA; ô số trang và select không có nhãn; cột thao tác của bảng không có tiêu đề (thêm chữ ẩn "Actions"); thanh tab con nằm trong landmark; thứ tự heading ở Pipeline Hub. **Chưa kiểm**: `OP Reconciliation` và `Overkill Analysis` (API giả chưa đủ để hai tab này hiển thị), các hộp thoại đang mở, và các trang với dữ liệu thật.
- **G7 Bảng (một phần)**: `scope="col"`, hàng bấm được có `tabindex` + Enter/Space + `aria-selected`, số dùng `tabular-nums`. Chưa căn phải số và chưa có `caption`.
- **G4**: hiện có xác nhận bằng `window.confirm` ở 6 nơi (xoá rule, mapping, pipeline, job, clear queue…); chỉ còn việc đồng bộ sang hộp thoại thống nhất, ưu tiên thấp.

**2026-10-22 · F3 bộ nút chuẩn, F4 form chuẩn (một phần)**
- **Hiện trạng**: 377 nút, đã có sẵn một quy ước ngầm (mặc định = chính, `.secondary`, `.ghost-button`, `.compact`), nhưng thiếu: nút danger, trạng thái disabled (nút bị vô hiệu trông y hệt nút thường vì nền không đổi), trạng thái loading, và 75 nút tự chỉnh bằng style inline.
- **F3**: component `Button` (`ui.tsx`) với 4 kiểu `primary | secondary | ghost | danger`, 2 cỡ (`md`, `sm`) và `loading` (spinner, khoá nút, `aria-busy`). CSS: nút `disabled` mờ còn 55% và con trỏ `not-allowed` (hover không đổi màu khi disabled; các nút đã có kiểu disabled riêng được giữ nguyên), `.danger` viền đỏ, mục tiêu chạm 44px và chữ 16px trên màn hình cảm ứng. Không đổi tên class cũ nên các nút chưa chuyển vẫn chạy.
- **Đã chuyển sang `Button`**: đăng nhập; lưu mapping AI; 2 nút import ở OP Input; lưu hàng loạt và lưu dán ở OP Reconciliation (`loading` thay cho chuỗi "...ing" tự chế); các nút **xoá** (rule, mapping OP, pipeline, mapping AI) và **Clear Completed / Clear Queue** ở Leakage (kiểu danger, bỏ emoji, thêm `aria-label` có tên đối tượng). Đã kiểm tra trên Chrome: đăng nhập chỉ gọi API 1 lần dù bấm lại, nút hiện `aria-busy` + spinner, nút disabled mờ, nút danger đỏ.
- **F4**: `Field` nhận thêm `error` và tự nối `aria-describedby` (gợi ý + lỗi), `aria-invalid`, `aria-required` vào ô nhập; lỗi hiện đỏ dưới ô với `role="alert"`. Hiện **chưa có form nào dùng `error`** (các form vẫn báo lỗi bằng banner), nên cần chuyển dần.
- **Chưa làm**: còn ~340 nút dùng class cũ, 70 nút còn style inline, và các nút chỉ có emoji (✏️, ⌛, 🔍…) → F6. `loading` mới có ở 6 nút (G3 còn nhiều).

**2026-10-22 · F6 thay emoji bằng icon SVG**
- Có **223 emoji** dùng làm icon trong code đang chạy (⚡ 40, 📦 14, 🔍 12, 🚀 12, 📋 10, ⚠️ 8, …). Mã chết (`components/analytics/*`, `DefectPreviewGrid`) không động tới.
- Viết codemod bằng TypeScript AST: emoji nằm trong **nội dung JSX** (125 chỗ) đổi thành `<Icon name="…" className="icon inline-icon" />`; emoji trong **chuỗi, template, thuộc tính và `<option>`** (53 chỗ, nơi không chèn được SVG) bị bỏ, giữ nguyên chữ. Thêm 20 icon vào `Icon` (refresh, warning, clock, play, package, clipboard, target, info, image, save, link, star, edit, trash, flask, folder, book, lock, help, check); các emoji còn lại dùng lại icon có sẵn. Thêm lớp `.inline-icon` (cỡ theo chữ, khoảng cách tới nhãn).
- Còn lại trong code đang chạy: dấu `✓` (37) và `✗` (11) dùng làm ký hiệu chữ, `✕`/`★` ở vài chỗ nhỏ, và một chú thích. Giữ nguyên có chủ đích vì là ký tự chữ, không phải emoji màu.
- Kiểm tra: `tsc`/build sạch, 0 tab tràn ngang, 0 lỗi axe trên 13 tab, hồi quy điều hướng/tìm kiếm/nút đều đạt; xem ảnh chụp tab Queries thấy icon đồng nhất. **Chưa xem bằng mắt từng tab**: chữ nút có icon dài hơn một chút nên hàng nút dày đặc có thể xuống dòng.
- Việc nhỏ còn lại: nút chỉ có icon dựa vào `title` làm tên (nên đổi sang `aria-label`).

**2026-10-22 · G1 bốn trạng thái cho danh sách**
- **Lỗi tìm thấy**: nhiều danh sách luôn hiện "No records." kể cả khi **đang tải** hoặc **tải thất bại** (lỗi chỉ hiện ở banner tự mất). Người dùng không phân biệt được "rỗng thật" với "không tải được".
- Thêm `useLoadStatus()` và kiểu `LoadStatus` (`ui.tsx`), cùng prop `status` / `error` / `onRetry` cho `DataTable`: **đang tải** → 3 dòng placeholder + `aria-busy`; **lỗi, chưa có dòng** → "This list could not be loaded." kèm lý do và nút *Try again* (`role="alert"`, không bao giờ hiện "No records"); **lỗi, đang có dòng cũ** → giữ dòng và hiện banner vàng "Could not refresh… rows are from the last successful load" kèm Retry; **rỗng thật** → thông điệp riêng như trước. Làm mới nền (polling) không bật placeholder nên không làm nháy.
- Đã nối vào **17 bảng/danh sách**: Audit, Verification History, Scans (danh sách + kết quả), AI Verification (mappings), Connections & Sources (2 bảng), Products (catalog), Storage (objects + defects), Queries (profiles, pipelines, executions). Ngoài `DataTable`: hàng đợi Leakage, danh sách Classifier Rules (cũng sửa: tìm không ra khác với chưa có rule nào), Dashboard (nút *Try again*, không còn hiện báo cáo cũ như thể mới sau khi tải lỗi).
- Kiểm tra trên Chrome (API giả trả 500/503, chậm 1.5s): placeholder khi tải, "No records." chỉ khi thật sự rỗng, lỗi → thông báo + Try again, Try again thành công, lỗi khi đã có dữ liệu → banner. Hồi quy: 0 lỗi axe, 0 tab tràn ngang.
- **Chưa làm**: các bảng trong Overkill Analysis (3 bảng + 2 biểu đồ + lưới ảnh), OP Reconciliation, Feature Trends và các bảng nằm trong hộp thoại khác vẫn dùng "No records." mặc định; `ImportTab` (không còn trong menu). Biểu đồ chưa có skeleton riêng khi đang tải.

**2026-10-22 · G1 (bổ sung Overkill Analysis) và G4 hộp thoại xác nhận**
- **G1 bổ sung**: Overkill Analysis nay có đủ trạng thái cho 2 biểu đồ (xu hướng, so sánh line: skeleton cùng chiều cao khi tải, lỗi + Try again, không còn hiện "No trend data available" khi thực ra là lỗi), lưới thumbnail (lỗi + Try again) và 2 bảng phân tích hệ thống/ứng viên. Thêm `LoadFailure` và `ChartSkeleton` dùng chung trong `ui.tsx`. Đã kiểm tra: skeleton khi tải chậm, thông báo lỗi, Try again gọi lại API.
- **G4**: `confirm.tsx` (`ConfirmProvider` + `useConfirm()`) thay `window.confirm` ở **6 nơi**: xoá mapping AI, xoá rule, xoá mapping OP, xoá pipeline, Clear Completed/Clear Queue ở Leakage, và cảnh báo lưu script có nguy cơ lọt lỗi ở Rule Optimizer. Hộp thoại cùng giao diện với app, **không đóng băng trang**, nút xác nhận gọi đúng tên việc sắp làm ("Delete rule", "Clear queue"…), nút đỏ cho thao tác không hoàn tác, **Cancel được focus sẵn** (Enter không xác nhận nhầm), Esc huỷ. Ngoài provider vẫn rơi về `window.confirm` nên không bao giờ xoá âm thầm. Đã kiểm tra trên Chrome: Esc và Cancel không gọi API, Confirm gọi `DELETE /leakage-queue/clear`, không còn hộp thoại gốc của trình duyệt.
- Còn dùng `window.confirm` duy nhất ở `DefectFeatureAnalyticsDashboard` (mã chết, không có trong bundle).

**2026-10-22 · Giai đoạn D (hoàn tất): đo từng tab nặng rồi chỉ sửa chỗ thật sự chậm**
Đo bằng Chrome, CPU throttle 4×, dữ liệu mẫu cỡ thật (bảng 100 dòng, 30 điểm biểu đồ, 24 thumbnail):

| Thao tác | Kết quả đo | Quyết định |
|---|---|---|
| Gõ ô Search, tab Products (100 dòng) | trước ~100 ms/phím, sự kiện chậm nhất 460–680 ms → **sau ~4 ms/phím, sự kiện chậm nhất 48 ms**, bảng vẽ lại 30 → 0 lần | đã sửa (D1–D3) |
| Gõ ô tìm kiếm ledger, OP Reconciliation (100 dòng) | **0 lần vẽ lại dòng** sau khi sửa, ~21 ms/phím | đã sửa (mới) |
| Đổi giữa 3 view, Overkill Analysis (2 biểu đồ + 24 thumbnail) | ~120 ms mỗi lần đổi (≈30 ms thật) | chấp nhận được, không tách tab |
| Gõ ô tìm thumbnail, Overkill Analysis | ~11–19 ms/phím | chấp nhận được |
| Gõ vào form lớn: modal pipeline (35 ô), modal Batch Setup (20 ô) | ~15 ms và ~5–7 ms mỗi phím | chấp nhận được |

- **OP Reconciliation**: các dòng bảng ledger tách thành component `RecordRows` bọc `memo` (cùng `classificationStyle` chuyển ra cấp module); ô tìm kiếm ledger dùng `DebouncedInput`.
- **Kết luận**: lag khi gõ nằm ở những bảng nhiều dòng có ô nhập chung component (Products, OP Reconciliation). Các tab/form còn lại đo ở mức vài chục ms ở throttle 4×, nên **không tách nhỏ `OverkillTab` / `QueriesTab`** (rủi ro cao, lợi ích không đo được). D6 (đổi style inline trong vòng lặp thành class) không còn cần cho mục tiêu hiệu năng.
- Hồi quy: 0 lỗi axe, 0 tab tràn ngang, Enter/Apply/Reset của ô tìm kiếm vẫn đúng.

**2026-10-22 · G3, F4 (một phần), số đo cuối**
- **G3**: codemod AST chuyển các nút tự dựng trạng thái "đang xử lý" (`disabled={busy}` + chữ `{busy ? 'Saving…' : …}`) sang `Button loading` ở 7 nút nữa (Test connection, Analyze/Refresh ở OP Reconciliation, Refresh Analysis ở Overkill, Test mapping ở Queries, tải nội dung ở Defect Inspector…). Nút có style inline hoặc class lạ được bỏ qua có chủ đích; còn khoảng 330 nút chưa có `loading` (đa số là nút điều hướng/lọc, không gọi API).
- **F4**: form *Station mapping* (AI Verification) chuyển từ một banner lỗi chung sang **lỗi theo từng ô** bằng `Field error`: mỗi ô báo đúng việc cần sửa ("Enter the machine code.", "Use 1-64 characters…"), tự **focus ô lỗi đầu tiên**, lỗi biến mất ngay khi sửa ô đó, nối `aria-describedby` / `aria-invalid`. Lỗi từ server vẫn hiện ở banner. Đã kiểm tra trên Chrome: gửi form trống không gọi API, sửa ô thì lỗi ô đó mất, giá trị hợp lệ mới gọi `PUT`. Các form khác (rule, pipeline, connection…) chưa chuyển.
- **Số đo cuối (gzip)**: tải màn hình đăng nhập **91 KB** (ban đầu 523 KB, mọi người dùng đều tải toàn bộ); mở Dashboard **293 KB** gồm ECharts 205 KB (mục tiêu ban đầu ≤ 250 KB chưa đạt do ECharts); toàn bộ JS cộng lại 397 KB vì tách thành 27 file, mỗi tab chỉ tải khi mở. Chưa tính nén ở server (B8, chưa chạy thử với backend thật).
- **Chưa làm / cần quyết định**: G8 (giờ 24h thống nhất): hiện `formatDate` dùng `toLocaleString()` và `new Date(value)`; nếu API trả giờ UTC **không có hậu tố Z** (MariaDB/EF trả `DateTime` kiểu Unspecified, và backend không có converter chung) thì giờ hiển thị đang **lệch theo múi giờ** của người xem. Sửa sẽ đổi giờ đang hiển thị nên cần xác nhận dữ liệu thật trước. E7 (tiêu đề bảng dính) không làm được đơn giản vì `.table-wrap` đã là vùng cuộn ngang.

**2026-10-22 · G6 độ tương phản trong hộp thoại**
- Chạy axe trên **11 hộp thoại đang mở** (thêm sản phẩm/defect, quét nhiều bảng, Batch Connections, New Pipeline, Batch Setup Pipelines, import AOI Rules, chạy scan…). 3 hộp thoại không mở được bằng kịch bản (tên nút khác), chưa kiểm.
- Lỗi chính: **chữ màu "giao diện tối" trên nền trắng** (`#93c5fd` tương phản 1.64:1, `#38bdf8`, `#f59e0b`, `#818cf8`, `#34d399`, `#94a3b8`, `#cbd5e1`, `#e2e8f0`) còn sót trong style inline của 2 modal Batch và QueriesTab. Script chỉ đổi màu chữ trong những khối style **không** có nền tối (giữ nguyên các ô code nền đen), 31 chỗ → màu đậm tương đương (`#1d4ed8`, `#0369a1`, `#b45309`, `#4f46e5`, `#047857`, `#475569`).
- Sửa kèm: ô JSON import AOI Rules chưa có nhãn (`aria-label`), vùng cuộn ở Batch Pipelines chưa focus được bằng bàn phím (`tabindex` + `role="group"`).
- Kết quả: lỗi trong hộp thoại từ **5 loại / ~60 vị trí → 2 vị trí** (một ô nhập chữ xám trên nền xám và một dòng chú thích). Hồi quy: 0 lỗi axe ở 13 tab, 0 tab tràn ngang. Giao diện hai modal Batch vẫn nguyên bố cục (một số khối trong modal vẫn dùng nền tối/xám kiểu cũ: lệch tông nhưng đọc được, là việc dọn giao diện riêng).

**2026-10-22 · G8 giờ UTC thống nhất và giai đoạn H phát hành**
- **G8**: theo xác nhận "giờ lưu là UTC", `format.ts` có `parseServerTime` (chuỗi **không có múi giờ** được đọc là UTC; có `Z` hoặc `+07:00` thì dùng đúng; ngày không giờ như `2026-10-21` giữ nguyên) và `formatDate` luôn ra **24h `YYYY-MM-DD HH:mm:ss` theo múi giờ của người xem**. Dùng chung cho `PipelineHub` (giờ rút gọn, "x phút trước") và "paused until" của AI Verification. Đã thử các dạng `...T08:00:00`, `...Z`, `+07:00`, có dấu cách, ngày trơn, chuỗi rác, rỗng, phần giây dài. Máy thử ở UTC+7 cho `08:00 UTC → 15:00`. **Chưa kiểm**: các cột lấy giờ từ bảng máy gốc (không phải trường `...Utc`) nếu những giá trị đó là giờ địa phương thay vì UTC.
- **H – kiểm tra**: `tsc` sạch, `oxlint` chỉ còn cảnh báo cũ, build sạch. Test backend **230/231 đạt**; 1 test (`AnalyzeClsf_RealD992File…`) lỗi vì `File.Exists(RealClsfPath)` trả false (cần file `.clsf` thật ở đường dẫn cố định trên máy), không liên quan thay đổi này. Chạy lại toàn bộ kiểm tra trình duyệt trên bản cuối (điều hướng, tìm kiếm, nút, form, trạng thái tải/lỗi, hộp thoại xác nhận, Dashboard): đạt; axe 0 lỗi, 0 tab tràn ngang.
- **H – bài học**: bản build đầu tiên dùng `VITE_API_BASE_URL=/api/v1` ngay trên Git Bash bị Windows đổi thành `C:/Program Files/Git/api/v1`, nên mọi lời gọi API sai địa chỉ. Bộ kiểm tra bắt được trước khi chép đi đâu. Đã build lại **không đặt biến này** (mặc định đã là `/api/v1`, đúng với `embed-frontend.ps1` và `build.ps1` chạy bằng PowerShell). Khi build tay bằng Git Bash đừng đặt biến này.
- **H – phát hành**: bản build mới đã chép vào `src/backend/AutoVerify.Api/wwwroot` và `artifacts/AutoVerify_release/wwwroot` (27 file assets, trùng khớp với `dist`, mọi file `index.html` tham chiếu đều có mặt). Bản cũ lưu ở `scratchpad\release-backup\` (thư mục tạm).
- **H – chưa làm**: `AutoVerify.Api.exe/.dll` trong `artifacts/AutoVerify_release` **vẫn là bản cũ**, chưa có nén Brotli/Gzip và cache `assets` (B8). Muốn có cần chạy `build.ps1` hoặc `dotnet publish` để tạo lại bản backend; chưa chạy thử server thật với MariaDB nên chưa xác nhận header `Content-Encoding` ngoài thực tế.

**2026-10-22 · Rà soát bằng mắt, đồng bộ chiều cao ô nhập**
- Xem ảnh chụp các tab có dữ liệu mẫu (Products, AI Verification…) ở 1440px: giờ 24h, icon, nút, token màu hiển thị nhất quán, không vỡ bố cục.
- Sửa: ô `select` cao hơn ô nhập chữ cùng hàng trong form (`.field`); nay cả hai cao 40px. Đã chạy lại axe (0 lỗi), quét tràn ngang (0), tìm kiếm (đạt) và **build lại, chép lại vào hai thư mục wwwroot** (trùng khớp `dist`).
- **Việc còn mở, đều không chặn**: chuyển các form còn lại sang `Field` có `error` (F4); skeleton dùng chung cho thẻ/lưới (F5); `PageHeader` dùng chung (F7); cache dữ liệu API (C3, lợi ích nhỏ vì tab đã được giữ trong bộ nhớ); kích thước ảnh của `/image-crawler/preview` (E2, cần backend); `content-visibility` cho khối dài (E5); publish lại backend để có nén và cache (B8).

**2026-10-22 · B8 kiểm chứng nén/cache, publish backend vào thư mục mới**
- Chưa ai trả lời việc ghi đè backend cũ, nên **không động tới** `artifacts/AutoVerify_release`. Backend được publish riêng bằng `dotnet publish` vào **`artifacts/autoverify-app`** (đầu ra chuẩn của `build.ps1`, thư mục mới, đã gồm `wwwroot` mới và `AutoVerify.Api.exe` có nén/cache).
- Backend thật cần MariaDB (`localhost:3306`, tự tạo schema và seed khi khởi động) nên **không chạy** để tránh sửa cơ sở dữ liệu thật. Thay vào đó dựng server thử dùng **đúng đoạn cấu hình** của `Program.cs` phục vụ chính `wwwroot` vừa publish, đo bằng `curl`:
  - `ReactEChartsImpl` (chunk lớn nhất): 620.953 B → **232.408 B (gzip)** / 259.622 B (brotli).
  - `assets/*`: `Cache-Control: public, max-age=31536000, immutable`; `index.html`: `no-cache`.
  - JSON của API (400 phần tử): 11.381 B → **1.843 B**.
  - Mức nén Brotli mặc định (Fastest) kém gzip một chút ở file lớn; có thể chỉnh `BrotliCompressionProviderOptions.Level` nếu cần.
- Việc còn lại cho bạn: khi muốn dùng bản mới, trỏ chạy `artifacts/autoverify-app/AutoVerify.Api.exe`, hoặc cho phép ghi đè `artifacts/AutoVerify_release` (cần dừng server đang chạy trước). Mới xác nhận cấu hình, **chưa xác nhận trên backend thật** (header `Content-Encoding` khi chạy cùng MariaDB).

**2026-10-22 · Cập nhật `artifacts/AutoVerify_release` (theo đồng ý của bạn)**
- Xác nhận không có tiến trình `AutoVerify.Api` đang chạy; **sao lưu toàn bộ** thư mục cũ (17 MB) vào `scratchpadelease-backup\AutoVerify_release-full` (thư mục tạm).
- So sánh với bản publish mới: chỉ `AutoVerify.Api.dll`, `.exe`, `.pdb` và `wwwroot` khác. Đã cập nhật đúng các file đó. **Giữ nguyên `appsettings.json`** của bản phát hành (khác bản mặc định, ví dụ cổng 543) và `appsettings.Local.example.json`; các DLL thư viện không đổi.
- Chạy thử nhẹ binary mới bằng `--export-bootstrap-sql` (chỉ xuất mô hình, không ghi vào cơ sở dữ liệu): khởi tạo xong dịch vụ và xuất 34 KB SQL, thoát mã 0. Thư mục `logs` do lần chạy thử tạo ra đã xoá.
- **Chưa kiểm**: chạy đầy đủ cùng MariaDB và xem header `Content-Encoding` thực tế (đã đo ở server thử với đúng cấu hình). Để hoàn tác: chép lại từ thư mục sao lưu.

**2026-10-22 · Đợt cuối: tên cho nút chỉ có icon, các mục không làm**
- **Truy cập**: codemod AST tìm nút chỉ có icon (không có chữ): 3 nút ở QueriesTab dựa vào `title` nay có thêm `aria-label`; **không còn nút icon nào thiếu tên**. Build lại, chạy lại axe (0 lỗi), tràn ngang (0), điều hướng, Dashboard: đạt. Bản build mới đã đồng bộ vào `src/backend/.../wwwroot`, `artifacts/AutoVerify_release/wwwroot` và `artifacts/autoverify-app/wwwroot`.
- **Không làm, có lý do**:
  - **C3 cache danh sách connections**: các tab mở cách nhau hơn vài chục giây nên cache gần như không trúng, lại có nguy cơ hiện trạng thái health cũ; lợi ích không đo được.
  - **E5 `content-visibility`**: có thể làm hỏng neo cuộn và `IntersectionObserver` của biểu đồ; không có chỗ nào đo thấy vấn đề vẽ ngoài khung nhìn.
  - **E7 tiêu đề bảng dính**: `.table-wrap` đã là vùng cuộn ngang nên `position: sticky` theo trang không hoạt động.
  - **F5 skeleton thẻ/lưới, F7 `PageHeader`**: đã có skeleton cho bảng, biểu đồ, tab và mỗi tab đã có tiêu đề nhất quán; gộp thành component chung chỉ đổi cấu trúc mà không thay đổi trải nghiệm.
  - **F4 các form còn lại**: dựa vào xác nhận `required` của trình duyệt, vẫn dùng được; chuyển dần khi sửa từng form.
  - **E2 kích thước ảnh preview**: cần xem phản hồi của backend `/image-crawler/preview`, ngoài phạm vi giao diện.

---

## 1. Mục tiêu đo được

Đo trên Chrome DevTools với **CPU throttling 4×** và mạng "Fast 3G" để giả lập máy yếu. Dữ liệu thật hoặc dữ liệu giả cỡ lớn (bảng 5.000+ dòng, 24 ảnh/trang, biểu đồ nhiều chuỗi).

| Chỉ số | Mục tiêu |
|---|---|
| JS tải lần đầu | Hiện **1.76 MB chưa nén / 518 KB gzip** trong một file (đo ở `dist/assets/index-CWNJJyPX.js`). Server hiện **không nén**, nên trình duyệt đang tải đủ 1.76 MB. Mục tiêu: bản đầu tiên (đăng nhập + Dashboard, đã gồm ECharts thu gọn) ≤ 250 KB gzip; các tab còn lại tải theo yêu cầu |
| Từ lúc mở trang đến lúc dùng được (TTI) | < 2 giây trên mạng LAN, < 4 giây ở throttle |
| Chuyển tab đã từng mở | Hiện ngay (< 100ms), dữ liệu cũ hiện trước, không chớp "Loading" |
| Gõ vào ô nhập / lọc | INP < 200ms, không có long task > 50ms |
| Cuộn bảng và lưới ảnh | 60 fps, không giật khi đang tải ảnh |
| Khi tab trình duyệt bị ẩn | Không còn request polling nào chạy |
| Lighthouse Accessibility | ≥ 95 |

---

## 2. Nguyên nhân lag và vấn đề giao diện tìm được trong code

Mức độ: 🔴 nặng · 🟠 vừa · 🟡 nhẹ

### 2.1 Hiệu năng

| # | Mức | Vấn đề | Vị trí |
|---|---|---|---|
| P1 | 🔴 | **Toàn bộ app nằm trong một file JS 1.76 MB.** `App.tsx` import cứng cả 16 tab, và không nơi nào dùng `React.lazy`. Trình duyệt phải tải và parse hết (kể cả ECharts, trang Classifier Rules, Storage…) trước khi hiện được màn hình đăng nhập. | `App.tsx:4-19`, `dist/assets/index-*.js` |
| P2 | 🔴 | **ECharts được kéo vào nguyên bản.** 14 file `import ReactECharts from 'echarts-for-react'`; đường import mặc định này nạp toàn bộ `echarts`. Dự án chỉ dùng 5 loại biểu đồ: `bar` (18 chỗ), `line` (16), `scatter` (9), `pie` (3), `heatmap` (1), cùng `visualMap` và `markLine`. | 14 file trong `components/analytics/`, `tabs/OverviewTab`, `OverkillTab`, `OpReconciliationTab` |
| P3 | 🔴 | **Chuyển tab là huỷ rồi dựng lại.** Mỗi tab chỉ render khi `currentTabId` khớp, nên rời đi là mất toàn bộ state (bộ lọc, trang đang xem, dữ liệu đã tải, vị trí cuộn) và vào lại phải gọi API từ đầu. Rất tốn với tab nặng như Overkill Analysis (47 `useState`). | `App.tsx:309-334` |
| P4 | 🟠 | **Các tab quá lớn, chứa quá nhiều state trong một component**: `OverkillTab` 2.809 dòng / 47 state, `QueriesTab` 2.522 dòng, `BatchPipelinesModal` 1.247 dòng. `OpReconciliationTab` (1.986 dòng) không phải tab riêng mà được nhúng trong **cả** `OpInputTab` và `OverkillTab`, nên Overkill Analysis kéo theo luôn nó. Gõ một ký tự vào bất kỳ ô lọc nào cũng vẽ lại cả tab, kể cả biểu đồ và lưới ảnh. | `tabs/OverkillTab.tsx:5`, `OpInputTab.tsx:4`, `QueriesTab.tsx`, `components/BatchPipelinesModal.tsx` |
| P5 | 🟠 | **Không có `React.memo`, `useDeferredValue`, `useTransition` ở bất kỳ đâu.** `DataTable` dựng lại mọi dòng mỗi lần component cha render; `rows` tạo mới mỗi lần nên chưa memo được ngay. | `ui.tsx:109-172`, toàn bộ `tabs/` |
| P6 | 🟡 | Biểu đồ: option **đã bọc `useMemo`** ở mọi nơi (tốt). Còn thiếu: mọi biểu đồ khởi tạo ngay cả khi nằm ngoài khung nhìn; dữ liệu lớn chưa bật chế độ `large` / `sampling`; animation vẫn chạy với dữ liệu lớn. | `components/analytics/*`, `OverkillTab.tsx:491,637`, `OverviewTab.tsx:24,88` |
| P7 | 🟠 | **Polling chạy cả khi tab trình duyệt bị ẩn**; chỉ `PipelineHub` kiểm tra `visibilityState`. Các nơi khác dùng `setInterval` thẳng, không chờ request trước xong (máy chậm thì request chồng nhau). Chi tiết: `LeakageOpsTab` gọi `getProgress` **mỗi 2.5s kể cả khi hàng đợi rỗng**; `AiVerificationTab` tải lại status + mappings mỗi 15s, luôn luôn; `DefectPreviewGrid` (1.5s) và `ProductsTab` (15s) đã chỉ chạy khi có việc đang chờ, chỉ thiếu điều kiện tab ẩn. | `LeakageOpsTab.tsx:150-160`, `AiVerificationTab.tsx:51-55`, `DefectPreviewGrid.tsx:224`, `ProductsTab.tsx:87-91`, `PipelineHub.tsx:208,228` |
| P8 | 🟠 | **Lưới ảnh defect (24–100 ảnh/trang) không có `loading="lazy"`, `decoding="async"`, kích thước cố định.** Khi không có `thumbnailUrl` thì lưới gọi `/api/v1/image-crawler/preview/{id}`; **cần kiểm tra** endpoint này trả ảnh cỡ nào (nếu là ảnh gốc thì rất nặng cho một ô thumbnail). Ảnh lỗi chỉ bị `display: none`, nên ô trống mà không có thông báo. | `OverkillTab.tsx:2002-2050`, `api.ts:515`, `ui.tsx:651,1023,1091`, `LeakageOpsTab.tsx:1203` |
| P9 | 🟠 | **`api()` không huỷ request khi rời tab / đổi bộ lọc** (chỉ một số hàm có nhận `signal`). Request cũ về sau có thể ghi đè kết quả mới (race). Không có cache, mỗi lần mở đều tải lại. | `api.ts:155`, các tab gọi API trong `useEffect` |
| P10 | 🟡 | `DataTable`/lưới render **toàn bộ dòng** của trang. Với `pageSize` 100 và cell chứa component nặng (badge, nút) thì tốn; `key={index}` dùng ở 11 chỗ. | `ui.tsx:109`, 11 chỗ `key={index}` |
| P11 | 🟡 | CSS: `transition: all` ở 12 chỗ (kích hoạt layout/paint không cần thiết), `backdrop-filter: blur` ở 1 chỗ, animation lặp vô hạn (`progress-stripes`, `highlight-pulse`, `skeleton-pulse`, `pulse-live`) chạy kể cả khi không nhìn thấy. `App.css` là một file 3.374 dòng / 88 KB nạp đồng bộ. | `App.css:980,1428,1802,1921,2098,2111,2801` |
| P12 | 🟡 | Khoảng **1.075 chỗ dùng `style={{...}}` inline** (OverkillTab 154, OpReconciliationTab 142, BatchPipelinesModal 128, ui.tsx 101…) → tạo object mới mỗi lần render, khó đồng bộ giao diện, không dùng được `:hover`/`:focus`. | các file ở cột bên |
| P13 | 🟠 | **Server không nén file tĩnh và không đặt cache.** `Program.cs` chỉ có `UseStaticFiles`, không có `UseResponseCompression`, không có `Cache-Control`. Bundle 1.76 MB đi nguyên vẹn qua mạng, và trình duyệt có thể tải lại sau mỗi lần mở. | `backend/AutoVerify.Api/Program.cs:218-224` |

### 2.2 UX/UI

| # | Vấn đề | Vị trí |
|---|---|---|
| X1 | **Bốn mức chữ rất nhỏ**: `font-size` 8–11px ở 150 chỗ trong CSS, cộng thêm 9–12px inline. Khó đọc, nhất là chữ tiếng Việt có dấu và màn hình công xưởng. | `App.css` (150 chỗ), inline style |
| X2 | **Màu và khoảng cách rải rác, không có token.** Hàng trăm mã hex (`#1e40af`, `#2563eb`, `#38bdf8`, `#0d1a32`…), nhiều bảng màu khác nhau giữa các tab (có tab nền tối `#020617`, có tab nền sáng). Giao diện lệch nhau giữa các phân hệ. | `App.css`, inline style |
| X3 | **Không có bộ component chuẩn cho form/nút**: `button`, `input` được style toàn cục, rồi mỗi tab tự chế thêm `className` và `style`. Có `.secondary`, `.ghost-button`, `.compact`… nhưng không có quy ước "một nút chính mỗi khối". | `App.css:1-20`, các tab |
| X4 | **Trạng thái đang tải kém**: chỉ chữ "Loading…" hoặc `⌛ Loading…` bằng emoji. Dữ liệu về thì bố cục nhảy. Không có skeleton. | `OverviewTab`, `OverkillTab:2035`, `ui.tsx:1018` |
| X5 | **Emoji làm biểu tượng** (⌛ 🔍 ✕) trong nút và nhãn: kích thước, màu, hiển thị khác nhau giữa các máy, trình đọc màn hình đọc sai. Đã có bộ `Icon` SVG nhưng chưa dùng thống nhất. | `OverkillTab`, `ui.tsx`, `BatchPipelinesModal` |
| X6 | **Thông báo (`NoticeBanner`) tự mất sau 6 giây**, kể cả thông báo lỗi. Lỗi quan trọng biến mất khi người dùng chưa kịp đọc. Mỗi lúc chỉ có một thông báo, thông báo mới đè thông báo cũ. | `App.tsx:193-197` |
| X7 | **Điều hướng 2 tầng nhưng tab con có thể tràn**: Operations có 5 tab con, Configuration có 6; thanh tab con `flex-wrap`, nên trên màn hình hẹp xuống dòng lộn xộn. Sidebar 240px cố định, chỉ có vài breakpoint (1100/700/430) và chưa thu gọn được. | `App.css:22-23, 56-60, 179-195` |
| X8 | **Không nhớ vị trí**: tải lại trang luôn về Dashboard; không có URL/hash cho từng tab nên không chia sẻ được link và nút Back của trình duyệt không dùng được. | `App.tsx:178-185` |
| X9 | **Truy cập (a11y)**: thanh tab con có `role="tablist"` / `role="tab"` nhưng thiếu `role="tabpanel"`, `aria-controls`, và chưa điều hướng được bằng phím mũi tên. (`Modal` **đã có** bẫy focus, Esc và trả focus, không cần làm lại.) Ô input khi focus vừa đổi màu viền vừa có `outline`; cần kiểm tra outline đủ rõ trên nền tối. | `App.tsx:282-300`, `ui.tsx:327-412` |
| X10 | **Màn hình đăng nhập hiện sẵn mật khẩu mặc định** (`defaultValue="admin!"`) và ghi chú "Temporary MVP credentials". | `App.tsx:160-171` |
| X11 | **Hành động nguy hiểm/không thể hoàn tác** (xoá, chạy lại hàng loạt, clear queue) chưa có hộp xác nhận thống nhất; mỗi tab xử lý một kiểu. | các tab Ops, Sources, Storage |
| X12 | **Bảng rộng không ghim cột**: bảng nhiều cột cuộn ngang mất cột định danh (Key ID); chỉ `pipeline-hub-table` có cột hành động dính phải. | `ui.tsx DataTable`, `App.css:3357` |
| X13 | **Tab `Import` không còn đường vào**: `ImportTab` vẫn được import và render khi `currentTabId === 'import'`, nhưng `SECTIONS` không có mục nào trỏ tới nó. Code thừa này vẫn bị đóng gói vào bundle. | `App.tsx:9, 313`, `types.ts:1550` |

---

## 3. Giai đoạn A — Đo hiện trạng (~0.5 ngày)

| ID | Việc |
|---|---|
| ✅ A1 | Lưu baseline: kích thước build (`npm run build`), Lighthouse (Performance, Accessibility, Best Practices) cho màn hình đăng nhập và Dashboard. |
| ⬜ A2 | Quay Performance trace 4× throttle cho 5 thao tác nặng nhất: mở Overview, mở Overkill Analysis, gõ vào ô lọc ở Overkill Analysis, lật trang lưới ảnh, mở OP Reconciliation. Ghi long task và INP. |
| ⬜ A3 | React DevTools Profiler (bật "Highlight updates") cho cùng 5 thao tác: ghi số lần render của tab và `DataTable`. |
| ⬜ A4 | Ghi vào bảng ở mục 10. |

**Tiêu chí xong:** có số liệu trước khi sửa cho mọi chỉ số ở mục 1.

---

## 4. Giai đoạn B — Giảm dung lượng tải (~1 ngày) · sửa P1, P2, P13, X13

| ID | Việc | File |
|---|---|---|
| ✅ B1 | `React.lazy` + `Suspense` cho **mọi tab** trong `App.tsx`. Màn hình đăng nhập và Dashboard (`OverviewTab`) là chunk đầu. Mỗi tab có `Suspense` riêng với khung xương (xem F5) để tiêu đề trang không nhảy. Các tab đang export dạng named (`export function OverkillTab`) nên dùng `lazy(() => import('./tabs/OverkillTab').then(m => ({ default: m.OverkillTab })))`. | `App.tsx:4-19, 309-334` |
| ✅ B2 | Tải trước chunk khi rê chuột hoặc focus vào mục điều hướng (`onPointerEnter` / `onFocus` gọi `import()`), và tải ngầm các tab còn lại khi trình duyệt rảnh (`requestIdleCallback`) sau khi đăng nhập. Chia nhỏ bundle mà chuyển tab vẫn tức thì. | `App.tsx:221-303` |
| ✅ B3 | **ECharts bản thu gọn, không phải viết lại biểu đồ**: tạo `src/lib/echarts.ts` dùng `echarts/core` và chỉ đăng ký những gì đang dùng: `BarChart`, `LineChart`, `ScatterChart`, `PieChart`, `HeatmapChart`, `GridComponent`, `TooltipComponent`, `LegendComponent`, `TitleComponent`, `VisualMapComponent`, `MarkLineComponent`, `CanvasRenderer` (thiếu gì thì build/console sẽ báo, bổ sung khi đó). 14 file đổi import sang `echarts-for-react/lib/core` và truyền `echarts={echarts}`. Đo lại mức giảm bằng `vite-bundle-visualizer`, không ước đoán trước. | `src/lib/echarts.ts` (mới), 14 file import |
| ✅ B4 | Component bọc `Chart` (dùng `ReactEChartsCore` bên trong): chỉ khởi tạo biểu đồ khi nó **vào khung nhìn** (`IntersectionObserver`), hiện `ChartSkeleton` trước đó. Option đã được `useMemo` ở nơi gọi nên `echarts-for-react` tự bỏ qua `setOption` thừa; không cần tự xử lý thêm. | `components/Chart.tsx` (mới) |
| ✅ B5 | Tách chunk vendor trong `vite.config.ts` (`manualChunks`: `react` + `react-dom`, `echarts` + `zrender`) để trình duyệt cache lâu giữa các lần phát hành. | `vite.config.ts` |
| ⬜ B6 | CSS: tách `App.css` theo phạm vi (shell và ui chung nạp ngay; CSS của tab nặng nạp theo chunk bằng `import './OverkillTab.css'` trong chính tab). Làm từng bước, không đổi tên class. | `App.css` → `styles/*.css` |
| ✅ B7 | **Xử lý `ImportTab` không còn đường vào (X13)**: hoặc thêm lại vào `SECTIONS`, hoặc gỡ import + nhánh render (giữ file để dùng lại sau). Chờ chốt ở mục 13. | `App.tsx:9, 313` |
| ✅ B8 | **Nén và cache ở server (P13)**: đây là việc backend nhỏ, đề xuất làm cùng: `AddResponseCompression` (Brotli + Gzip, `EnableForHttps = true`) và `UseResponseCompression()` đặt trước `UseStaticFiles`; trong `StaticFileOptions.OnPrepareResponse`, file dưới `/assets/` (tên có hash) đặt `Cache-Control: public, max-age=31536000, immutable`, `index.html` đặt `no-cache`. Nếu không muốn đụng backend thì ghi vào mục 12. | `backend/AutoVerify.Api/Program.cs:218-224` |

**Tiêu chí xong:** bản tải đầu ≤ 250 KB gzip (hiện 518 KB). Dữ liệu đi qua mạng thực sự được nén (header `Content-Encoding` trong tab Network). Mở một tab chưa từng vào chỉ tải thêm chunk của tab đó. Biểu đồ vẫn ra cùng hình như trước (so ảnh chụp từng biểu đồ trước/sau).

---

## 5. Giai đoạn C — Giữ tab và dữ liệu khi chuyển (~1 ngày) · sửa P3, P9, X8

| ID | Việc | File |
|---|---|---|
| ✅ C1 | **Giữ tab đã mở trong bộ nhớ** (keep-alive): sau khi một tab được mở lần đầu, nó vẫn nằm trong DOM nhưng bị ẩn (`hidden`) thay vì bị huỷ. Giới hạn: tối đa ~5 tab gần nhất, tab cũ hơn bị giải phóng. Tab ẩn không chạy polling (xem E3). **Lưu ý ECharts:** biểu đồ nằm trong khối ẩn có kích thước 0, nên khi tab hiện lại phải gọi `resize()` cho các biểu đồ trong tab đó (đặt trong component `Chart` ở B4, lắng nghe `TabActiveContext`). | `App.tsx:309-334`, `components/KeepAlive.tsx` (mới) |
| ✅ C2 | Tab ẩn không tính toán: truyền `active` xuống tab (context `TabActiveContext`) để tab tạm dừng polling, không cập nhật biểu đồ, và **tải lại ngầm khi quay lại nếu dữ liệu cũ hơn N phút**, kèm chỉ báo "Updating…". Khi tải lại lỗi thì báo lỗi, không im lặng giữ dữ liệu cũ như thể mới. | `KeepAlive.tsx`, các tab |
| ⬜ C3 | Hook `useApiQuery(key, fn, { staleMs })`: cache theo khóa trong bộ nhớ, **huỷ request bằng `AbortController`** khi đổi tham số hoặc rời tab, bỏ qua phản hồi đến muộn, gộp các request trùng nhau. Dùng cho các danh sách dùng chung (connections, products filter options, facets) đang bị nhiều tab tải lặp lại. | `src/lib/useApiQuery.ts` (mới), `api.ts:155` |
| ⬜ C4 | `api()` nhận `signal` mặc định cho mọi hàm (hiện chỉ một số hàm có). | `api.ts` |
| ✅ C5 | **Điều hướng có URL**: đồng bộ phân hệ và tab con với `location.hash` (`#/operations/leakage-ops`). Nút Back/Forward dùng được, tải lại trang giữ nguyên chỗ, chia sẻ được link. Không thêm thư viện router. Dùng hash vì backend đã có `MapFallback` về `index.html` nhưng hash không phụ thuộc vào cấu hình đó. | `App.tsx:178-185, 206-212` |
| ⬜ C6 | Nhớ lựa chọn nhẹ của người dùng (cỡ trang, bộ lọc hay dùng) trong `localStorage`, có `try/catch`. | các tab |

Lưu ý: dữ liệu **thao tác** (kết quả verify, hàng đợi đang xử lý) không được cache như dữ liệu tĩnh. Mọi thứ đang chạy nền vẫn phải hiển thị đúng trạng thái thật khi quay lại tab.

**Tiêu chí xong:** chuyển qua lại giữa Overkill Analysis và OP Reconciliation giữ nguyên bộ lọc, trang và vị trí cuộn, không chớp "Loading". Tải lại trang ở một tab con vẫn ở tab đó.

---

## 6. Giai đoạn D — Chặn render thừa ở các tab lớn (~1.5 ngày) · sửa P4, P5, P6, P12 (một phần)

Làm tuần tự theo mức tốn kém: `OverkillTab` → `OpReconciliationTab` → `QueriesTab` → `BatchPipelinesModal` → `ProductsTab`.

| ID | Việc | File |
|---|---|---|
| ✅ D1 | **Tách vùng bộ lọc ra component con** có state riêng; chỉ khi bấm "Apply" (hoặc sau debounce 300ms) mới báo lên cha. Gõ vào ô lọc không còn vẽ lại biểu đồ và lưới ảnh. | các tab lớn |
| ✅ D2 | Tách mỗi vùng nặng (biểu đồ xu hướng, so sánh line, lưới ảnh thumbnail, bảng, phần `OpReconciliationTab` nhúng bên trong) thành component riêng bọc `React.memo`, nhận props ổn định (`useMemo` cho dữ liệu đã biến đổi, `useCallback` cho handler). `OpReconciliationTab` nhúng trong `OverkillTab` chỉ được render khi người dùng mở phần đó, và tải lazy. | `OverkillTab.tsx:5, 491, 637` |
| ✅ D3 | `DataTable`: bọc `React.memo`, tách `TableRow` memo theo `row.id`; tạo `rows` bằng `useMemo` ở nơi gọi. Thay `key={index}` bằng khoá ổn định. | `ui.tsx:109-172`, 11 chỗ `key={index}` |
| ✅ D4 | Ô tìm kiếm và bộ lọc cục bộ dùng `useDeferredValue` / `startTransition` để gõ luôn mượt dù danh sách dài. | các tab có lọc phía client |
| ✅ D5 | Biểu đồ (option đã có `useMemo`, giữ nguyên): với dữ liệu lớn (> 2.000 điểm) bật `large: true` cho scatter/bar, `sampling: 'lttb'` cho line, `progressive`, và tắt animation (`animation: false`). | `components/analytics/*`, `OverkillTab`, `OverviewTab` |
| ❌ D8 | (Tuỳ chọn, cân nhắc sau D1–D3) Thử **React Compiler** (`babel-plugin-react-compiler`) để tự động memo thay cho việc thêm `useMemo`/`useCallback` bằng tay. Chỉ bật nếu build và lint sạch và Profiler cho thấy có lợi; bật theo từng thư mục trước. | `vite.config.ts` |
| ⬜ D6 | Chuyển `style={{...}}` inline thành class CSS ở các chỗ nằm trong vòng lặp (lưới ảnh, hàng bảng, thẻ), tránh tạo hàng trăm object mỗi lần render. Làm cùng với F (design tokens). | `OverkillTab` (154), `OpReconciliationTab` (142), `BatchPipelinesModal` (128), `ui.tsx` (101) |
| ✅ D7 | `Modal`: chỉ gắn vào cây DOM khi mở; nội dung nặng của modal (`BatchPipelinesModal`, `RuleOptimizerModal`) tải lazy (`React.lazy`). | `ui.tsx:327`, các modal lớn |

Không thêm thư viện quản lý state và không thêm virtualization cho đến khi đo cho thấy bảng thật sự cần (bảng đã phân trang). Nếu đo thấy vẫn giật ở trang ≥ 200 dòng thì cân nhắc `content-visibility: auto` cho hàng bảng trước.

**Tiêu chí xong:** Profiler: gõ vào ô lọc chỉ render lại component ô lọc. Không còn long task > 50ms khi gõ và khi lật trang.

---

## 7. Giai đoạn E — Ảnh, bảng, polling (~1 ngày) · sửa P7, P8, P10, P11

| ID | Việc | File |
|---|---|---|
| ⏳ E1 | Component `LazyImage`: `loading="lazy"`, `decoding="async"`, `width`/`height` hoặc `aspect-ratio` cố định (không nhảy bố cục), khung xương trong lúc tải, **thông báo lỗi trong ô** ("Không tải được ảnh" + nút thử lại) thay cho `display: none`. Dùng ở lưới thumbnail, `ImageViewer`, `DefectFeatureInspector`, `LeakageOpsTab`, `BatchPipelinesModal`. | `components/LazyImage.tsx` (mới), 6 nơi dùng `<img>` |
| ⬜ E2 | Kiểm tra kích thước ảnh mà `/api/v1/image-crawler/preview/{id}` trả về. Lưới thumbnail phải dùng ảnh thu nhỏ; ảnh gốc chỉ tải khi mở phóng to. Nếu API chỉ có ảnh gốc thì ghi vào mục 12 (không sửa ở frontend). Không cần tự giới hạn số ảnh tải cùng lúc: `loading="lazy"` cộng với giới hạn kết nối của trình duyệt là đủ, trừ khi đo thấy server bị quá tải. | `OverkillTab.tsx:2002-2005`, `api.ts:515` |
| ✅ E3 | Hook `usePolling(fn, ms, { enabled })`: dừng khi `document.hidden` **hoặc** tab không active (C2), chờ lần trước xong mới hẹn lần sau (`setTimeout` nối tiếp), huỷ request khi tắt, giãn dần khi lỗi liên tiếp, chạy ngay khi quay lại. Thay cho 6 `setInterval` hiện có. | `src/lib/usePolling.ts` (mới), 6 nơi ở mục P7 |
| ✅ E4 | `LeakageOpsTab`: `loadItems`/`loadBatches` đã chỉ chạy khi `isProcessing`, giữ nguyên. Sửa `pollProgress` đang chạy mỗi 2.5s cả khi hàng đợi rỗng: khi rảnh thì giãn ra 15–30s, khi đang chạy thì 2.5s. `AiVerificationTab`: chỉ poll khi tab đang mở **và** đang hiện trên màn hình. | `LeakageOpsTab.tsx:119-160`, `AiVerificationTab.tsx:51-55` |
| ⬜ E5 | `content-visibility: auto` + `contain-intrinsic-size` cho các khối dài ngoài khung nhìn (panel biểu đồ, lưới ảnh). | `App.css` |
| ⏳ E6 | CSS: thay `transition: all` bằng danh sách thuộc tính cụ thể (12 chỗ); animation lặp vô hạn chỉ chạy khi phần tử đang hiển thị và bọc `@media (prefers-reduced-motion: no-preference)`; bỏ hoặc giới hạn `backdrop-filter` (chỉ giữ ở modal scrim nếu cần). | `App.css:980,1428,1802,1921,2098,2111,2801` |
| ⬜ E7 | Bảng rộng: ghim cột định danh (Key ID) bên trái và cột hành động bên phải, tiêu đề cột dính khi cuộn dọc (`position: sticky`). Tuỳ chọn trong `DataTable` (`stickyFirstColumn`). | `ui.tsx DataTable`, `App.css` |

**Tiêu chí xong:** ẩn tab trình duyệt 1 phút: không có request polling trong Network. Lật trang lưới ảnh không giật, không nhảy bố cục khi ảnh về.

---

## 8. Giai đoạn F — Nền tảng giao diện chuẩn (~1.5 ngày) · sửa X1, X2, X3, X5, P12

Mục tiêu: một ngôn ngữ giao diện cho cả 4 phân hệ. Giữ nhận diện hiện tại (sidebar tối xanh navy, nền sáng cho nội dung, xanh `#2563eb` làm màu hành động); chỉ gom lại cho nhất quán.

| ID | Việc | File |
|---|---|---|
| ⏳ F1 | **Design tokens** trong `:root`: màu (nền, bề mặt, viền, chữ chính/phụ, hành động, và 3 màu trạng thái OK / NG / cảnh báo), khoảng cách (4/8/12/16/24), bo góc (6/8/12), bóng (hai mức), cỡ chữ, độ cao điều khiển. Quy đổi dần các mã hex hiện có sang token (làm theo từng tab, không đổi hàng loạt trong một lần). | `index.css`, `App.css` |
| ✅ F2 | **Thang chữ tối thiểu 12px** (mặc định 14px, tiêu đề 16/20px). Bỏ toàn bộ `font-size` 8–11px ở CSS và inline; dòng cao ≥ 1.5 để không cắt dấu tiếng Việt. Dữ liệu so sánh giữa các dòng (ID, số đo, thời gian) dùng `font-variant-numeric: tabular-nums`. | `App.css` (150 chỗ), inline style |
| ✅ F3 | **Bộ nút chuẩn**: `btn-primary` (một nút chính mỗi khối), `btn-secondary`, `btn-ghost`, `btn-danger`; cỡ `sm / md`; trạng thái `hover / active / focus-visible / disabled / loading` (spinner + khoá nút). Thay các `className`/`style` tự chế ở các tab. | `App.css`, `ui.tsx` (`Button`) |
| ⏳ F4 | **Bộ form chuẩn**: `Field` (đã có, mở rộng) có nhãn, gợi ý, thông báo lỗi tại chỗ (`aria-describedby`, `aria-invalid`), cỡ điều khiển cao 36px (44px trên màn hình cảm ứng `pointer: coarse`). | `ui.tsx:41` |
| ⬜ F5 | **Skeleton** dùng chung (`Skeleton`, `TableSkeleton`, `CardSkeleton`, `ChartSkeleton`) đúng kích thước nội dung thật. Animation nhấp nháy bọc `prefers-reduced-motion`. | `ui.tsx` |
| ✅ F6 | **Thay emoji bằng `Icon` SVG** (⌛ 🔍 ✕ ✓ ⚠…). Thêm các icon còn thiếu (`search`, `warning`, `check`, `refresh`, `external`, `image`). Icon chỉ trang trí có `aria-hidden`. | `ui.tsx Icon`, `OverkillTab`, `BatchPipelinesModal`, `LeakageOpsTab` |
| ⬜ F7 | **Component dùng chung còn thiếu**: `PageHeader` (tiêu đề + mô tả + tối đa 1 hành động chính), `Toolbar` (hàng bộ lọc), `ConfirmDialog`, `Tooltip`, `StatCard` thống nhất với `MetricCard`. | `ui.tsx` |
| ⏳ F8 | **Điều hướng**: sidebar thu gọn được (chỉ hiện icon) và tự thu gọn dưới 1100px; thanh tab con cuộn ngang (bỏ `flex-wrap`), có bóng mờ ở mép khi còn tab bị che, tab đang chọn tự cuộn vào khung nhìn; trên điện thoại sidebar thành ngăn kéo. Chân sidebar đang ghi cứng "API /api/v1 Connected" với chấm xanh dù không kiểm tra gì: đổi thành trạng thái thật (gọi health) hoặc bỏ. | `App.tsx:221-303`, `App.css:22-60, 179-195` |
| ✅ F9 | Màn hình đăng nhập: bỏ `defaultValue` mật khẩu và dòng "Temporary MVP credentials" (hoặc chỉ hiện ở bản dev qua `import.meta.env.DEV`), thêm nút hiện/ẩn mật khẩu, trạng thái lỗi và đang đăng nhập rõ ràng. | `App.tsx:150-173` |

Quy ước áp dụng: khi chạm vào một tab ở bất kỳ giai đoạn nào thì quy đổi luôn tab đó sang token và component chuẩn, để không phải duyệt lại.

**Tiêu chí xong:** không còn `font-size` dưới 12px; không còn emoji làm biểu tượng; ≥ 90% màu trong CSS đi qua token; mỗi khối chỉ có một nút `btn-primary`.

---

## 9. Giai đoạn G — Chuẩn UX: trạng thái, phản hồi, bàn phím, truy cập (~1.5 ngày) · sửa X4, X6, X9, X11, X12

| ID | Việc | File |
|---|---|---|
| ✅ G1 | **Bốn trạng thái cho mọi danh sách/biểu đồ**: đang tải (skeleton) · rỗng (câu giải thích + hành động tiếp theo) · lỗi (nói rõ không tải được + nút Thử lại, **không** hiện như "không có dữ liệu") · có dữ liệu. `EmptyState` hiện có mở rộng thêm biến thể lỗi. | `ui.tsx:669`, các tab |
| ⏳ G2 | **Hệ thống thông báo (toast)**: xếp chồng nhiều thông báo; thành công tự ẩn sau 4s; **lỗi giữ nguyên đến khi người dùng đóng** (có nút đóng); `role="status"` / `role="alert"`; tạm dừng đếm giờ khi rê chuột. Giữ API `onNotice` hiện có để không phải sửa hàng loạt. | `ui.tsx:414`, `App.tsx:186-197` |
| ⏳ G3 | **Phản hồi tức thì**: mọi nút gọi API khoá lại và hiện trạng thái đang xử lý ngay khi bấm (< 100ms); thao tác dài (scan, migrate) hiện tiến độ thật từ API, không vẽ thanh tiến độ giả. | các tab |
| ✅ G4 | **`ConfirmDialog` thống nhất** cho hành động không hoàn tác (xoá kết nối, xoá query profile, xoá file, clear queue, chạy lại hàng loạt, migrate). Nêu rõ hệ quả ("Sẽ xoá 24 mục. Không thể hoàn tác."), nút huỷ là mặc định có focus. | `SourcesTab`, `QueriesTab`, `StorageTab`, `LeakageOpsTab`, `OverkillOpsTab` |
| ✅ G5 | **Bàn phím**: thanh tab con theo mẫu WAI-ARIA (mũi tên trái/phải, Home/End, `role="tabpanel"` + `aria-controls`/`aria-labelledby`). `Modal` đã có bẫy focus, Esc và trả focus; chỉ thêm tuỳ chọn chặn Esc/bấm nền khi đang gửi request. Phím tắt `/` để focus ô tìm kiếm ở những tab có ô tìm kiếm. | `App.tsx:282-300`, `ui.tsx:327-412` |
| ✅ G6 | **Tương phản và focus**: chữ đọc được ≥ 4.5:1 (đặc biệt chữ xám nhạt `#94a3b8` trên nền trắng và chữ nhỏ trên nền tối); viền focus nhìn thấy trên mọi điều khiển (không dựa vào đổi màu viền); trạng thái không chỉ dựa vào màu (badge OK/NG có chữ hoặc icon). | `App.css`, `ui.tsx` |
| ⏳ G7 | **Bảng**: số được căn phải, dùng mono + `tabular-nums`; tiêu đề cột có `scope="col"`; có `<caption>` ẩn cho trình đọc màn hình; hàng bấm được có `tabIndex=0` và phản hồi Enter/Space; cột rộng cắt chữ có `title` đầy đủ. | `ui.tsx DataTable` |
| ✅ G8 | **Giờ và số**: một hàm định dạng giờ dùng chung (24h, múi giờ người xem; giờ từ server hiểu là UTC nếu thiếu `Z`), một hàm định dạng số (`toLocaleString`); giá trị không có hiện `—`, không hiện `0`. | `format.ts` |
| ✅ G9 | **Responsive**: kiểm tra từng tab ở 1920 / 1440 / 1024 / 768 / 390px; không tràn ngang trang (chỉ bảng được cuộn ngang trong khung của nó); modal trên điện thoại thành bottom sheet cuộn được. | tất cả |
| ✅ G11 | **Error boundary cho từng tab**: một tab lỗi render chỉ hiện khung "Tab này gặp lỗi" + nút Tải lại tab, không làm trắng cả ứng dụng và thanh điều hướng vẫn dùng được. | `components/TabPane.tsx` |
| ✅ G10 | Chạy kiểm tra axe (hoặc Lighthouse a11y) cho 4 phân hệ và các modal đang mở; sửa lỗi nghiêm trọng. | tất cả |

**Tiêu chí xong:** mọi danh sách/biểu đồ có đủ 4 trạng thái; lỗi không tự biến mất; điều hướng toàn bộ bằng bàn phím được; Lighthouse Accessibility ≥ 95.

---

## 10. Giai đoạn H — Kiểm tra và phát hành (~0.5 ngày)

Sau **mỗi** giai đoạn:
1. `npm run build` (gồm `tsc -b`) và `npm run lint` sạch.
2. Đo lại các chỉ số ở mục 1, ghi vào bảng dưới.
3. Chạy thử trên Chrome ở 1920 / 1440 / 390px (cảm ứng).
4. Chạy lại luồng chính từ đầu đến cuối: đăng nhập → Overkill Operations (dán danh sách ID, verify) → Scan Executions → Verification History → Products (xem defect) → Overkill Analysis (biểu đồ, lưới ảnh, phóng to ảnh) → Feature Trends → Connections / Query Profiles → Audit.
5. So sánh ảnh chụp trước/sau cho các biểu đồ ECharts (giai đoạn B).

Khi phát hành: build rồi cập nhật `src/backend/AutoVerify.Api/wwwroot` và `artifacts/AutoVerify_release/wwwroot` (hai nơi đang chứa bản build).
Thư mục `src/web/dist` và `wwwroot` hiện không còn khớp tên file bundle (`index-CWNJJyPX.js` so với `index-C6wpH1QC.js`), cần build lại để đồng bộ.

### Bảng số đo (điền khi làm)

| Chỉ số | Baseline | Sau B | Sau C | Sau D | Sau E | Sau F–G |
|---|---|---|---|---|---|---|
| JS tải lần đầu (KB, gzip) | 523 | 77 | 77 | 77 | 77 | 91 (đăng nhập), 293 (có biểu đồ) |
| TTI (throttle) | | | | | | |
| Chuyển lại tab đã mở (ms) | | | | | | |
| Gõ ô Search ở Products: ms script mỗi phím (throttle 4×) | ~100 | | | ~4 | | |
| Long task khi lật trang lưới ảnh | | | | | | |
| Request polling khi tab ẩn 1 phút | có | | | | không | |
| Lighthouse Performance / Accessibility | | | | | | |

---

## 11. Không nằm trong phạm vi

- Không đổi API, không sửa backend, **trừ B8** (nén + cache file tĩnh, vài dòng cấu hình trong `Program.cs`) nếu được đồng ý. Phần còn lại ghi ở mục 12.
- Không đổi thuật ngữ nghiệp vụ (Overkill, Leakage, Station Judge, VP…) và logic xác minh.
- Không đổi nhận diện chính (sidebar navy, màu hành động xanh); không thêm dark mode.
- Không thêm thư viện router, state toàn cục, hay UI kit. Dùng React có sẵn, CSS thuần và token.
- Không refactor lại toàn bộ file lớn trong một lần: chỉ tách những phần cần để chặn render (D) và dùng component chuẩn (F).

---

## 12. Việc ghi nhận cho backend (không chặn)

- Ảnh thumbnail: lưới Overkill nên luôn nhận `thumbnailUrl` đã thu nhỏ. Hiện khi thiếu, frontend gọi `/image-crawler/preview/{id}` (cần xác nhận endpoint này trả ảnh cỡ nào).
- Nếu B8 không làm ở plan này: bật nén Brotli/Gzip và `Cache-Control` cho `assets/*` trong `Program.cs:218-224`.
- Tuỳ chọn: endpoint trả `ETag` / `If-None-Match` cho danh sách dùng chung (connections, filter options) để C3 tải lại nhẹ hơn.

---

## 13. Câu hỏi cần chốt trước khi làm

1. **Máy người dùng chạy giao diện này** là loại nào (CPU/RAM, trình duyệt)? Quyết định mức throttle khi đo.
2. **Kích thước dữ liệu thật**: một trang bảng thường bao nhiêu dòng, một lần lưới ảnh bao nhiêu ảnh, biểu đồ nhiều nhất bao nhiêu điểm? (Quyết định có cần virtualization hay không.)
3. **Giữ tab trong bộ nhớ (C1)**: đồng ý giữ tối đa ~5 tab gần nhất không? Đổi lại bộ nhớ trình duyệt cao hơn một chút.
4. **Màn hình đăng nhập (F9)**: bỏ hẳn mật khẩu điền sẵn, hay chỉ ẩn ở bản production?
5. **Có cần thu gọn sidebar thành thanh icon (F8)** hay giữ 240px cố định như hiện nay?
6. **Tab `Import` (B7)**: thêm lại vào menu, hay gỡ khỏi app?
7. **Nén và cache ở server (B8)**: cho phép sửa vài dòng trong `Program.cs` không? Đây là bước giảm thời gian tải nhiều nhất (1.76 MB → khoảng 0.5 MB qua mạng) mà không phải đổi code giao diện.
