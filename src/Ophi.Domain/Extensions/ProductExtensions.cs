using Ophi.Domain.Entities;

namespace Ophi.Domain.Extensions;

public static class ProductExtensions
{
    extension(Product product)
    {
        public ProductUrl? GetPrimaryUrl() =>
            // Id breaks CreatedAt ties (one SaveChanges stamps a bulk import identically), so the
            // primary URL does not depend on the order EF loads the collection in.
            product.ProductUrls.OrderBy(pu => pu.CreatedAt).ThenBy(pu => pu.Id).FirstOrDefault();
    }
}
