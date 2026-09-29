# MyNewsDigest ("Tổng hợp tin tức – Nguyễn Hải Anh") — Project Context

Web đọc báo nhanh **dùng cá nhân**: thu thập tin từ nhiều báo, gom cụm theo sự kiện, AI chấm điểm "độ hot" + chuyên mục + trong nước/quốc tế, hiển thị ảnh + các bài của từng báo kèm link gốc (KHÔNG tóm tắt).
Người dùng giao tiếp bằng tiếng Việt — trả lời bằng tiếng Việt.

## Quyết định quan trọng của người dùng (đừng làm ngược lại)
- **KHÔNG tóm tắt** — chỉ tổng hợp tin. AI chỉ trả `category` + `region` + `score` + `reason`. Tiêu đề sự kiện = tiêu đề bài gốc.
- **KHÔNG chạy nền, KHÔNG tự cập nhật, KHÔNG Telegram bot** (2026-09-28, vì quota AI có hạn). Tin chỉ cập nhật khi người dùng bấm "Cập nhật ngay". Đã xóa `BackgroundJobs/`, endpoint cron, cài đặt tự cập nhật.
- **Ưu tiên Cloudflare Workers AI**, Gemini chỉ dự phòng.
- Giao diện: dark, xanh lá (#22c55e) + vàng (#facc15), logo riêng; logo báo thay cho tên báo ở dòng bài.

## Trạng thái hiện tại
- [x] Giai đoạn 1 (lấy tin, gom cụm, AI chấm điểm, web + đăng nhập) — test thật.
- [x] Giai đoạn 2 (migrations, cài đặt DB, đọc sau/đã đọc/"có cập nhật", bộ lọc, quản lý nguồn, bản tin) — test API thật.
- [x] Giai đoạn 3 Telegram — **BỎ**.
- [~] Giao diện (2026-09-28): logo, theme, ảnh sự kiện, logo báo, thẻ featured, ô tìm kiếm có nút, ẩn tin đã đọc tức thì, lọc trong nước/quốc tế theo nội dung (AI `region`).
  - [ ] Chưa tự xem trên trình duyệt / điện thoại — chờ người dùng góp ý.
- [x] Git: repo https://github.com/anhnh-99nd/my-news-digest (nhánh `main`), commit đầu 2026-09-29.
- [x] Đã build bản publish Release → `publish/MyNewsDigest.zip` (~18 MB, thư mục `publish/` bị gitignore), chạy thử Production OK trên máy.
- [ ] Chưa upload/chạy thật trên MonsterASP (người dùng tự upload zip).
- [!] **Quota Cloudflare free = 10.000 neurons/ngày**; ngày 2026-09-28 đã hết do test nhiều (llama-3.3-70b tốn). Hết quota → embedding lỗi (gom cụm rơi về Jaccard tiêu đề) + chấm điểm chuyển Gemini (hay 503). Reset 7h sáng VN. Nếu vẫn thiếu: đổi `Cloudflare:ChatModel` sang model nhỏ hoặc giảm `Ai:MaxClustersPerRun`. **Khi test hạn chế số lượt refresh.**

> Cập nhật mục này mỗi khi xong một phần việc, kèm ghi chú ngắn về cấu trúc code/quyết định mới.

## Sơ đồ code (tóm tắt để khỏi đọc lại source)
- `MyNewsDigest.slnx` → `MyNewsDigest.Web/` (1 project ASP.NET Core MVC .NET 10).
- `Program.cs`: DI, cookie auth (API trả 401, không redirect), SQLite `App_Data/news.db` (đường dẫn tuyệt đối theo ContentRoot), `Database.Migrate()` (migrations ở `Data/Migrations`; đổi entity → `dotnet ef migrations add <Tên>` trong `MyNewsDigest.Web/`; tool khai báo ở `dotnet-tools.json` gốc repo → `dotnet tool restore`), `SourceSeeder.Seed` chỉ THÊM nguồn mặc định từ appsettings nếu DB chưa có key. CLI: `dotnet run -- hash-password <mk>`. **Không có hosted service.**
- `Models/Entities.cs`: `Source` (FeedUrl có thể nhiều link, phân tách xuống dòng), `Article` (Url unique, Embedding float32 bytes, ImageUrl: null = chưa thử, "" = không có), `Cluster` (= 1 sự kiện: Title, Category, IsInternational (AI region, null = chưa chấm), AiScore, SourceCount, TrendMatch, NeedsScoring, LastUpdatedAt = giờ đăng bài mới nhất, LastChangedAt = lúc thêm bài mới nhất, ReadAt, IsSaved; IsRead = ReadAt ≥ LastChangedAt, HasUpdate), `AppSetting` (key "app" → JSON `AppSettings`), `Digest` + `DigestItem` (bản chụp, ClusterId SetNull). `Models/Categories.cs`: thoi-su, the-gioi, kinh-te, cong-nghe, the-thao, giai-tri, phap-luat, doi-song, giao-duc, suc-khoe, khac.
- `Services/NewsPipeline.cs` (1 lượt = 1 lần bấm cập nhật): tải RSS song song (mọi link của nguồn) → lọc bài mới trong `WindowHours` (+ bổ sung ảnh RSS cho bài cũ) → embedding Workers AI `@cf/baai/bge-m3` → gom cụm (cosine ≥ `SimilarityThreshold` 0.72, fallback Jaccard tiêu đề ≥ 0.5) → Google Trends → AI chấm cụm `NeedsScoring` (ưu tiên nhiều báo, tối đa `Ai:MaxClustersPerRun`) → og:image cho cụm chưa có ảnh (≤60/lượt, `ImageFetcher`) → xóa bài cũ hơn `RetentionDays` (trừ cụm IsSaved).
- `Services/RefreshCoordinator.cs`: 1 lượt tại 1 thời điểm + trạng thái tiến trình; sau pipeline gọi `DigestService.CreateScheduledIfDueAsync` (chốt bản tin nếu đã qua khung giờ hôm nay mà chưa có).
- `Services/HotScore.cs`: Display = AI + 0.25×(số báo−1, tối đa 6) + 0.5 nếu trending (kẹp 1–10); Rank = Display × 0.5^(tuổi giờ/18).
- AI qua `IClusterScorer` (`Services/AiPrompt.cs`: prompt + schema + parser dùng chung). Thứ tự theo cài đặt `AiProviders` (mặc định cloudflare → gemini): `CloudflareChatClient` (model `Cloudflare:ChatModel` = llama-3.3-70b-instruct-fp8-fast, JSON mode, lô 20), `GeminiClient` (responseSchema, lô 25). Lỗi/hết quota → chuyển nhà cung cấp kế, chấm lại đúng lô.
- `Services/SettingsService.cs`: `AppSettings` (RetentionDays, DefaultMinScore, AiProviders, DigestTimes "HH:mm" giờ VN, DigestSize, ViewMode list|digest|both, HideRead).
- `Services/DigestService.cs`: top `DigestSize` cụm theo Display, khoảng từ bản tin trước (6–24h). `Services/TextHelpers.cs`: `VnTime`, `ClusterTitle` (ưu tiên tiêu đề có dấu), `ClusterImage` (ưu tiên báo trong nước, bài sớm nhất; BBC 240→800px), `LogoUrl` (favicon Google s2 theo tên miền), `FeedUrls`, `Fold` (bỏ dấu để tìm), `Slug`.
- `FeedFetcher.cs` (RSS/Atom, gzip, bỏ utm_, ảnh từ enclosure/media/img), `EmbeddingClient.cs`, `ImageFetcher.cs`, `ViteAssets.cs` (dev → Vite server, prod → `wwwroot/app/manifest.json`).
- API: `POST /api/auth/login|logout`, `GET /api/auth/me`; `GET /api/news?hours&categories&sources&scope(all|domestic|international — theo NỘI DUNG)&minScore&q&view(all|unread|saved)&limit`, `GET /api/news/meta`; `POST|DELETE /api/clusters/{id}/read`, `POST /api/clusters/read {ids}`, `POST|DELETE /api/clusters/{id}/save`; `GET|POST /api/sources`, `PUT|DELETE /api/sources/{id}`, `POST /api/sources/test` (nhiều link, trả bài mới nhất); `GET|PUT /api/settings`; `GET|POST /api/digests`, `GET /api/digests/{id}`; `POST /api/refresh`, `GET /api/refresh/status`; `GET /api/ping`.
- ClientApp: `App.tsx` → `Login` / `Shell.tsx` (header Brand + nút Cập nhật ngay/poll 2s, tab hash `#news|digest|saved|sources|settings`), `NewsTab.tsx` (thanh tìm kiếm + bộ lọc, lưu bộ lọc localStorage), `ClusterCard.tsx` (ảnh, điểm, logo các báo, danh sách bài, Đọc sau/Đã đọc, featured cho tin #1), `DigestTab.tsx`, `SourcesTab.tsx`, `SettingsTab.tsx`, `Brand.tsx` (LogoMark, Brand, SourceLogo), `styles.css`. `wwwroot/favicon.svg` = logo.

## Chạy & publish
- Bí mật (đã cấu hình trên máy dev qua user-secrets): `Auth:Username` (= `nhakings9x`), `Auth:PasswordHash`, `Gemini:ApiKey`, `Cloudflare:AccountId`, `Cloudflare:ApiToken`. KHÔNG ghi vào file trong repo.
- Dev: terminal 1 `cd MyNewsDigest.Web/ClientApp && npm run dev`; terminal 2 `cd MyNewsDigest.Web && dotnet run` → http://localhost:5080 (cổng này trước từng bị app MyChinesesChess chiếm).
- Publish: `dotnet publish MyNewsDigest.Web -c Release` (tự `npm install` + `npm run build`). Trên MonsterASP đặt bí mật bằng biến môi trường `Auth__Username`, `Auth__PasswordHash`, `Gemini__ApiKey`, `Cloudflare__AccountId`, `Cloudflare__ApiToken`. Không cần cron vì không có job nền.

## Nguồn RSS — kiểm tra thực tế 2026-09-28
- OK: VnExpress, Tuổi Trẻ, Thanh Niên, Dân Trí, VietnamNet (`/rss/thoi-su|the-gioi|kinh-doanh|giai-tri.rss`; `tin-tuc.rss` chết từ 2022), VTC (`/rss/thoi-su.rss` + `the-gioi.rss`), Kenh14 (`/rss/home.rss`), CafeF (`/home.rss`), Vietnamplus (gzip), BBC Tiếng Việt, BBC World.
- Tắt: Người Lao Động (mọi feed ngừng từ 18/9/2026), Znews & PLO (không còn RSS), Lao Động (chặn bot bằng cookie JS), VOA (chưa tìm URL), CNN (feed cũ), Reuters/AP (không có RSS công khai).

## Ghi chú
- Chỉ dùng cá nhân → không đăng lại toàn văn, chỉ tiêu đề + ảnh + link gốc.
- Chưa làm: tín hiệu "vị trí trang chủ / Đọc nhiều" khi tính điểm hot.
- Cần .NET 10 SDK + Node.js ≥ 20 trên máy (Windows).
