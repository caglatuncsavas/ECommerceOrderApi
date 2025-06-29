Bu proje, **saatlik 5 token limit** olan external API'ler için **akıllı token yönetimi** sağlar. Her 5 dakikada sipariş listesi senkronizasyonu yaparken token limitine takılmayı önler.

## 🔥 Problem

- **İhtiyaç**: Her 5 dakikada sipariş listesi sorgusu (12 kez/saat)
- **Limit**: Token alımı için 5 istek/saat sınırı
- **Çelişki**: 12 > 5 😱 **Rate Limit Aşımı!**

## ✅ Çözüm Mimarisi

### 1. **Smart Token Caching & Rate Limit Protection**
```csharp
// Thread-safe token cache
private static TokenResponse? _cachedToken;
private static readonly SemaphoreSlim _semaphore = new(1, 1);

// Rate limit tracking
private const int MaxRequestsPerHour = 5;
private readonly TimeSpan _rateLimitWindow = TimeSpan.FromHours(1);
```

### 2. **Proactive Token Renewal**
```csharp
// Token süresinin 10 dakika öncesinde otomatik yenileme
public bool ShouldRenew => DateTime.UtcNow >= CreatedAt.AddSeconds(ExpiresIn - 600);
```

### 3. **Dual Background Services**
- **TokenRenewalBackgroundService**: Her 5 dakikada token durumu kontrolü
- **OrderSyncBackgroundService**: Her 5 dakikada sipariş senkronizasyonu

## 📁 Implementasyon Dosyaları

```
Services/
├── TokenService.cs                    # Ana token yönetimi (cache + rate limit)
├── TokenRenewalBackgroundService.cs   # Otomatik token yenileme
├── OrderSyncBackgroundService.cs      # Otomatik sipariş sync
└── Responses/
    └── TokenResponse.cs               # Token modeli

V1/Controllers/
└── TokenManagement.cs                 # Test & monitoring endpoints

Program.cs                             # Service registration + test user
```

## ⚙️ Konfigürasyon

### appsettings.json
```json
{
  "ExternalApi": {
    "TokenEndpoint": "https://api.example.com/oauth/token",
    "OrdersEndpoint": "https://api.example.com/api/orders",
    "ClientId": "your-client-id",
    "ClientSecret": "your-client-secret",
    "UseMockResponse": true
  },
  "TestUser": {
    "Email": "testuser@test.com",
    "Password": "Test123!"
  }
}
```

### Dependency Injection (Program.cs)
```csharp
// Token Management Services
builder.Services.AddScoped<ITokenService, TokenService>();

// Background Services  
builder.Services.AddHostedService<TokenRenewalBackgroundService>();
builder.Services.AddHostedService<OrderSyncBackgroundService>();

// Named HttpClient for token requests
builder.Services.AddHttpClient("TokenService", client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});
```

## 🚀 Otomatik Başlatma

Uygulama başladığında:

1. **✅ Test kullanıcısı otomatik oluşturulur**
2. **⏰ 10 saniye sonra** - Token renewal service başlar
3. **⏰ 30 saniye sonra** - Order sync service başlar
4. **🔄 Her 5 dakikada** - Token kontrolü ve sipariş senkronizasyonu

## 🎯 Token Lifecycle

```mermaid
graph TD
    A[App Start] --> B[İlk Token Al]
    B --> C[Memory Cache]
    C --> D[Background Service]
    D --> E{Token Expire?}
    E -->|Hayır| F[Cached Token Kullan]
    E -->|10dk Kala| G[Proactive Renewal]
    G --> C
    F --> H[Sipariş API Çağrısı]
    H --> I[5 Dakika Bekle]
    I --> D
    
    style B fill:#e1f5fe
    style C fill:#f3e5f5
    style G fill:#e8f5e8
    style H fill:#fff3e0
```

## 📊 Performance Optimizasyonu

### ❌ Önceki Durum (Hatalı)
```
Her API çağrısı → Yeni token = 12 token/saat
└── Rate limit aşımı ❌
└── API çağrıları başarısız ❌
```

### ✅ Şimdiki Durum (Optimized)
```
1 token → 60 dakika kullanım = 1 token/saat
├── 10 dakika buffer ile proactive renewal ✅
├── Thread-safe concurrent access ✅
├── Background service ile async management ✅
└── Fallback mechanism ✅
```

## 🧪 Test Endpoints

### Otomatik Test User
Uygulama başlatıldığında console'da göreceksiniz:
```
🎯 Test kullanıcısı oluşturuldu: testuser@test.com - ID: 12345678-1234-1234-1234-123456789abc
📝 Test için kullanın: GET /api/v1/orders?userId=12345678-1234-1234-1234-123456789abc
```

### API Endpoints

#### **Token Management**
```bash
# Token durumunu kontrol et
GET /api/v1/tokenmanagement/status

# Rate limit durumunu kontrol et
GET /api/v1/tokenmanagement/rate-limit

# Manuel token yenileme (test için)
POST /api/v1/tokenmanagement/force-renew
```

#### **Orders (Token Entegreli)**
```bash
# Sipariş listesi (Token otomatik yönetimi ile)
GET /api/v1/orders?userId={USER_ID}

# Yeni sipariş oluştur  
POST /api/v1/orders
Content-Type: application/json
{
    "userId": "12345678-1234-1234-1234-123456789abc",
    "items": [
        {
            "productId": 1,
            "quantity": 2
        }
    ]
}
```

## 🔄 Rate Limit Management

### Token Request Tracking
```csharp
// Saatlik window takibi
private static DateTime _lastRequestTime = DateTime.MinValue;
private static int _requestCount = 0;

// Rate limit kontrolü
public bool CanRequestToken()
{
    DateTime now = DateTime.UtcNow;
    
    if (now - _lastRequestTime > _rateLimitWindow)
    {
        _requestCount = 0; // Reset counter
    }
    
    return _requestCount < MaxRequestsPerHour;
}
```

### Error Handling
```csharp
// Rate limit aşımında exception
if (!CanRequestToken())
{
    throw new InvalidOperationException("Rate limit exceeded for token requests");
}

// Controller'da fallback
catch (InvalidOperationException ex) when (ex.Message.Contains("Rate limit"))
{
    // Local DB'den fallback response
    return await GetOrdersFromLocalDatabase(userId, cancellationToken);
}
```

## 📈 Monitoring & Logs

### Önemli Log Mesajları
```
✅ Cached token kullanılıyor. Expires: 2025-06-29 23:37:30
🔄 Token yenileniyor (10 dakika buffer)...
⚠️ Token süresi dolmuş, yenisi alınıyor...
🆕 İlk token alımı yapılıyor...
❌ Rate limit aşıldı! Son 1 saat içinde 5 istek yapıldı (Max: 5)
✅ Sipariş senkronizasyonu tamamlandı. 25 sipariş alındı, süre: 1250ms
```

### Background Service Status
```
🚀 Token Renewal Background Service başlatıldı
🚀 Order Sync Background Service başlatıldı
🔍 Token durumu kontrol ediliyor...
✅ Token kontrolü tamamlandı
🔄 Sipariş senkronizasyonu başlatılıyor...
```

## 🛡️ Güvenlik & Best Practices

### Thread Safety
```csharp
// SemaphoreSlim ile thread-safe access
await _semaphore.WaitAsync();
try
{
    // Token operations
}
finally
{
    _semaphore.Release();
}
```

### Configuration Security
```json
// Production'da environment variables kullanın:
{
  "ExternalApi": {
    "ClientId": "${EXTERNAL_API_CLIENT_ID}",
    "ClientSecret": "${EXTERNAL_API_CLIENT_SECRET}"
  }
}
```

### Error Resilience
- **Rate limit tracking** ile 5 istek/saat sınırını aşmama
- **Proactive renewal** ile token expiry önleme
- **Fallback mechanisms** ile service degradation
- **Structured logging** ile monitoring support

## 🎉 Sonuç

Bu token yönetimi çözümü ile:

- ✅ **Rate limit problemi çözüldü** (12 istek → 1 token/saat)
- ✅ **Zero-downtime** token yenileme (proactive renewal)
- ✅ **Production-ready** error handling ve logging
- ✅ **Scalable architecture** (background services)
- ✅ **Test-friendly** (otomatik test user + mock responses)
- ✅ **Monitoring support** (health check endpoints)

**🎯 Artık her 5 dakikada güvenle API çağrısı yapabilirsiniz!**

---

## 🔧 Quick Start

```bash
# 1. Uygulamayı başlat
dotnet run

# 2. Console'dan User ID'yi kopyala
# 3. Test et
curl "http://localhost:5268/api/v1/orders?userId=USER_ID"
curl "http://localhost:5268/api/v1/tokenmanagement/status"
```

**Token yönetimi otomatik çalışır, siz sadece API'yi kullanın!** ⚡ 
