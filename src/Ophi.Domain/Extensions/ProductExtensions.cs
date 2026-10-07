using Ophi.Domain.Entities;

namespace Ophi.Domain.Extensions;

public static class ProductExtensions
{
    extension(Product product)
    {
        public ProductUrl? GetPrimaryUrl() =>
            product.ProductUrls.OrderBy(pu => pu.CreatedAt).FirstOrDefault();
    }
}
