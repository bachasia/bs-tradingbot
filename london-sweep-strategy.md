# 🇬🇧 London Sweep Strategy — Hướng Dẫn Giao Dịch

> **Chiến lược giao dịch theo cú quét thanh khoản phiên London, kết hợp bộ lọc xu hướng H4 và động lượng H1.**

---

## Bước 1 — Xác định London Range (03:00–09:00 EST)

Ngay khi phiên London kết thúc lúc **09:00 EST**, vẽ hai đường ngang:

- 📈 **High London** — Mức giá cao nhất trong phiên 03:00–09:00 EST
- 📉 **Low London** — Mức giá thấp nhất trong phiên 03:00–09:00 EST

Đây gọi là **"London Range"** — vùng giá tham chiếu chính cho toàn bộ chiến lược.

> ⚠️ Nhớ điều chỉnh giờ theo DST mùa đông / mùa hè.

---

## Bước 2 — Kiểm tra độ rộng London Range

```
Độ rộng = High London - Low London
```

| Kết quả | Hành động |
|---|---|
| ≥ 50 point | ✅ Tiếp tục sang Bước 3 |
| < 50 point | 🚫 **Dừng lại — Không giao dịch hôm nay** |

---

## Bước 3 — Xác định xu hướng khung H4

So sánh **EMA20** và **EMA50** trên biểu đồ H4:

| Điều kiện | Xu hướng H4 | Hành động được phép |
|---|---|---|
| EMA20 > EMA50 | 📈 Tăng (Bullish) | **Chỉ vào lệnh LONG (Mua)** |
| EMA20 < EMA50 | 📉 Giảm (Bearish) | **Chỉ vào lệnh SHORT (Bán)** |

> 🔴 **Quy tắc cứng**: Tuyệt đối không giao dịch ngược xu hướng H4.

---

## Bước 4 — Kiểm tra động lượng khung H1

Lúc **09:00 EST**, so sánh hai cây nến H1:

- Nến đóng cửa lúc **08:00 EST**
- Nến đóng cửa lúc **03:00 EST** (cách 6 tiếng)

| Điều kiện | Động lượng | Xác nhận thiết lập |
|---|---|---|
| Đóng cửa 08:00 > Đóng cửa 03:00 | ✅ Dương | Chỉ xác nhận lệnh **Mua** |
| Đóng cửa 08:00 < Đóng cửa 03:00 | ❌ Âm | Chỉ xác nhận lệnh **Bán** |

> ⚠️ **Cả H4 VÀ H1 phải đồng nhất.** Nếu H4 tăng nhưng H1 động lượng âm → **Không vào lệnh.**

---

## Bước 5 — Quan sát cú quét thanh khoản (Sweep)

### 5.1 — Kiểm tra trước (09:00–09:29 EST)

- Nếu giá **đã quét** qua High hoặc Low London trong khoảng 09:00–09:29 → 🚫 **Không vào lệnh**
- Nếu giá **chưa quét** → ✅ Tiếp tục theo dõi

### 5.2 — Cửa sổ giao dịch (09:30–11:00 EST)

Theo dõi từng nến **M15** đóng cửa trong khung giờ này.

#### 📈 Lệnh LONG — cần H4 tăng + H1 động lượng dương

```
Low nến M15 < Low London - 5 point   →  Đâm xuống quét thanh khoản
VÀ
Nến M15 đóng cửa TRÊN mức Low London →  Từ chối và đảo chiều ✅
```

#### 📉 Lệnh SHORT — cần H4 giảm + H1 động lượng âm

```
High nến M15 > High London + 5 point  →  Đâm lên quét thanh khoản
VÀ
Nến M15 đóng cửa DƯỚI mức High London →  Từ chối và đảo chiều ✅
```

> ⚠️ **Quan trọng**: Cú quét và đảo chiều phải xảy ra trên **cùng một cây nến M15**.  
> Nếu giá phá vỡ High/Low nhưng đóng cửa ngoài vùng → Đó là **Breakout**, không phải Sweep → **Không vào lệnh.**

---

## Bước 6 — Checklist xác nhận trước khi vào lệnh

Kiểm tra toàn bộ danh sách trước khi thực thi:

- [ ] Vùng giá London ≥ 50 pts?
- [ ] Xu hướng H4 khớp với hướng giao dịch?
- [ ] Động lượng H1 khớp với hướng giao dịch?
- [ ] Cú quét xảy ra trong khung 09:30–11:00 EST?
- [ ] Hôm nay chưa thực hiện giao dịch nào?

> 🚦 Bất kỳ mục nào trả lời **"Không"** → **Bỏ qua giao dịch.**

> ✅ **Tất cả đều khớp** → Vào lệnh ngay khi **nến Sweep đóng cửa**.

---

## Bước 7 — Tính Entry, Stop Loss, Take Profit

> Tất cả lệnh đều **RISK 1%** tài khoản (khi giá chạm SL, chỉ mất 1%).

### 📈 Lệnh LONG

| Điểm | Công thức |
|---|---|
| **Entry** | Giá đóng cửa nến Sweep |
| **Stop Loss** | Low nến Sweep − 8 point |
| **Take Profit** | Entry + (0.65 × Độ rộng London Range) |

### 📉 Lệnh SHORT

| Điểm | Công thức |
|---|---|
| **Entry** | Giá đóng cửa nến Sweep |
| **Stop Loss** | High nến Sweep + 8 point |
| **Take Profit** | Entry − (0.65 × Độ rộng London Range) |

---

## Bước 8 — Quản lý giao dịch

> 🕒 **Nếu đến 15:00 EST** mà lệnh chưa chạm SL hoặc TP → **Đóng lệnh theo giá thị trường.**

Đây là thời điểm **cắt lệnh cuối ngày bắt buộc** — không giữ lệnh qua giờ này.

---

## Tổng quan luồng quyết định

```
London đóng cửa (09:00 EST)
        │
        ▼
[Range ≥ 50 pts?] ──── Không ──→ 🚫 Dừng
        │ Có
        ▼
[H4 xu hướng?] ──→ Xác định Long/Short
        │
        ▼
[H1 động lượng khớp?] ──── Không ──→ 🚫 Dừng
        │ Có
        ▼
[09:00–09:29: Giá chưa quét?] ──── Đã quét ──→ 🚫 Dừng
        │ Chưa quét
        ▼
[09:30–11:00: Tìm nến Sweep M15]
        │
        ▼
[Nến quét + đóng cửa đảo chiều?] ──── Không ──→ ⏳ Chờ tiếp
        │ Có
        ▼
✅ VÀO LỆNH → Đặt SL & TP → Đóng lệnh trước 15:00 EST
```

---

*Chiến lược này chỉ cho phép **tối đa 1 giao dịch/ngày**. Kỷ luật là yếu tố quan trọng nhất.*
