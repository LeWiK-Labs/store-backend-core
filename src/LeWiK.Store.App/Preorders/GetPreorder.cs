using LeWiK.Store.App.Common.Messaging;
using LeWiK.Store.App.Common.Persistence;
using LeWiK.Store.App.Common.Results;
using LeWiK.Store.App.Preorders.Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace LeWiK.Store.App.Preorders;

public sealed record GetPreorderQuery(Guid ProductVariantId) : IQuery<PreorderResponse>;

public sealed class GetPreorderHandler(StoreDbContext db)
    : IRequestHandler<GetPreorderQuery, Result<PreorderResponse>>
{
    public async Task<Result<PreorderResponse>> Handle(GetPreorderQuery request, CancellationToken ct)
    {
        var preorder = await db.Set<Preorder>()
            .FirstOrDefaultAsync(p => p.ProductVariantId == request.ProductVariantId, ct);

        return preorder is null
            ? PreorderErrors.NotFound(request.ProductVariantId)
            : ConfigurePreorderHandler.Map(preorder);
    }
}