using Ophi.Domain.Entities;
using Ophi.Domain.Enums;

namespace Ophi.TestHelpers;

/// <summary>
/// Fluent builders for test entities. Avoids the rote
/// <c>new Product { Id = Guid.NewGuid(), UserId = ..., Name = ..., Currency = "USD", Status = Active }</c>
/// boilerplate repeated across handler / integration / API tests.
///
/// Each builder is mutable in its fluent state and constructs the entity once via <c>Build()</c>
/// (or implicit conversion). This indirection is required because most entities have
/// <c>init</c>-only properties that can't be mutated after construction.
/// </summary>
public static class TestEntityFactory
{
    public static ProductBuilder Product(Guid userId) => new(userId);
    public static AlertBuilder Alert(Guid productId, Guid userId) => new(productId, userId);
    public static ProductUrlBuilder ProductUrl(Guid productId) => new(productId);
    public static UserBuilder User(Guid? id = null) => new(id ?? Guid.NewGuid());

    // Backwards-compatible tuple convenience used by existing callers.
    public static (Product product, ProductUrl productUrl) CreateProduct(string name, Guid userId)
    {
        var product = Product(userId).Named(name).Build();
        var url = ProductUrl(product.Id).WithUrl($"https://example.com/{Guid.NewGuid()}").Build();
        return (product, url);
    }

    public static Alert CreateAlert(Guid productId, Guid userId, decimal targetPrice, AlertCondition condition = AlertCondition.Below) =>
        Alert(productId, userId).WithTarget(targetPrice).WithCondition(condition).WithReference(100m).Build();

    public sealed class ProductBuilder
    {
        private Guid _id = Guid.NewGuid();
        private readonly Guid _userId;
        private string _name = "Test Product";
        private string _currency = "USD";
        private ProductStatus _status = ProductStatus.Active;
        private decimal? _currentPrice;
        private decimal? _previousPrice;
        private string? _imageUrl;
        private bool _favourite;
        private Guid? _groupId;

        internal ProductBuilder(Guid userId) => _userId = userId;

        public ProductBuilder WithId(Guid id) { _id = id; return this; }
        public ProductBuilder Named(string name) { _name = name; return this; }
        public ProductBuilder Priced(decimal? current, decimal? previous = null)
        {
            _currentPrice = current;
            _previousPrice = previous;
            return this;
        }
        public ProductBuilder WithStatus(ProductStatus status) { _status = status; return this; }
        public ProductBuilder WithCurrency(string currency) { _currency = currency; return this; }
        public ProductBuilder WithImage(string? url) { _imageUrl = url; return this; }
        public ProductBuilder Favourite(bool isFavourite = true) { _favourite = isFavourite; return this; }
        public ProductBuilder InGroup(Guid? groupId) { _groupId = groupId; return this; }

        public Product Build() => new()
        {
            Id = _id,
            UserId = _userId,
            Name = _name,
            Currency = _currency,
            Status = _status,
            CurrentPrice = _currentPrice,
            PreviousPrice = _previousPrice,
            ImageUrl = _imageUrl,
            IsFavourite = _favourite,
            ComparisonGroupId = _groupId
        };

        public static implicit operator Product(ProductBuilder b) => b.Build();
    }

    public sealed class AlertBuilder
    {
        private Guid _id = Guid.NewGuid();
        private readonly Guid _productId;
        private readonly Guid _userId;
        private decimal _target = 50m;
        private decimal _reference = 100m;
        private string _currency = "USD";
        private AlertCondition _condition = AlertCondition.Below;
        private bool _isActive = true;
        private DateTime? _lastTriggered;
        private int _triggerCount;
        private Product? _product;
        private User? _user;

        internal AlertBuilder(Guid productId, Guid userId)
        {
            _productId = productId;
            _userId = userId;
        }

        public AlertBuilder WithId(Guid id) { _id = id; return this; }
        public AlertBuilder WithTarget(decimal target) { _target = target; return this; }
        public AlertBuilder WithReference(decimal reference) { _reference = reference; return this; }
        public AlertBuilder WithCondition(AlertCondition condition) { _condition = condition; return this; }
        public AlertBuilder WithCurrency(string currency) { _currency = currency; return this; }
        public AlertBuilder Inactive() { _isActive = false; return this; }
        public AlertBuilder WithProduct(Product product) { _product = product; return this; }
        public AlertBuilder WithUser(User user) { _user = user; return this; }
        public AlertBuilder LastTriggered(DateTime? when) { _lastTriggered = when; return this; }
        public AlertBuilder WithTriggerCount(int count) { _triggerCount = count; return this; }

        public Alert Build() => new()
        {
            Id = _id,
            ProductId = _productId,
            UserId = _userId,
            TargetPrice = _target,
            ReferencePrice = _reference,
            Currency = _currency,
            Condition = _condition,
            IsActive = _isActive,
            LastTriggeredAt = _lastTriggered,
            TriggerCount = _triggerCount,
            Product = _product!,
            User = _user!
        };

        public static implicit operator Alert(AlertBuilder b) => b.Build();
    }

    public sealed class ProductUrlBuilder
    {
        private Guid _id = Guid.NewGuid();
        private readonly Guid _productId;
        private string _url;
        private decimal? _currentPrice;
        private string? _selector;
        private ProductUrlStatus _status = ProductUrlStatus.Active;
        private DateTime? _lastCheckedAt;
        private int _failureCount;
        private bool _outOfStock;

        internal ProductUrlBuilder(Guid productId)
        {
            _productId = productId;
            _url = $"https://example.com/{Guid.NewGuid():N}";
        }

        public ProductUrlBuilder WithId(Guid id) { _id = id; return this; }
        public ProductUrlBuilder WithUrl(string url) { _url = url; return this; }
        public ProductUrlBuilder Priced(decimal? current) { _currentPrice = current; return this; }
        public ProductUrlBuilder WithSelector(string? selector) { _selector = selector; return this; }
        public ProductUrlBuilder WithStatus(ProductUrlStatus status) { _status = status; return this; }
        public ProductUrlBuilder LastCheckedAt(DateTime? when) { _lastCheckedAt = when; return this; }
        public ProductUrlBuilder WithFailureCount(int count) { _failureCount = count; return this; }
        public ProductUrlBuilder OutOfStock(bool oos = true) { _outOfStock = oos; return this; }

        public ProductUrl Build() => new()
        {
            Id = _id,
            ProductId = _productId,
            Url = _url,
            Currency = "USD",
            CurrentPrice = _currentPrice,
            Selector = _selector,
            Status = _status,
            LastCheckedAt = _lastCheckedAt,
            FailureCount = _failureCount,
            IsOutOfStock = _outOfStock
        };

        public static implicit operator ProductUrl(ProductUrlBuilder b) => b.Build();
    }

    public sealed class UserBuilder
    {
        private readonly Guid _id;
        private string _email;
        private string _name = "Test User";
        private string _passwordHash = "hash";

        internal UserBuilder(Guid id)
        {
            _id = id;
            _email = $"user-{id:N}@example.com";
        }

        public UserBuilder WithEmail(string email) { _email = email; return this; }
        public UserBuilder Named(string name) { _name = name; return this; }
        public UserBuilder WithPasswordHash(string hash) { _passwordHash = hash; return this; }

        public User Build() => new()
        {
            Id = _id,
            Email = _email,
            Name = _name,
            PasswordHash = _passwordHash
        };

        public static implicit operator User(UserBuilder b) => b.Build();
    }
}
