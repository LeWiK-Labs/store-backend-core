using LeWiK.Store.App.Common.Messaging;
using LeWiK.Store.App.Common.Persistence;
using LeWiK.Store.App.Common.Results;
using LeWiK.Store.App.Preorders.Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace LeWiK.Store.App.Preorders;

// Ends a drop. Deliberately a step someone takes, not a side effect of releasing an order: the
// drop closing is a fact about the drop (the merchandise arrived, the window is over), while a
// release is a fact about one buyer's order — letting the first release close the drop would end
// it for everyone else on a stranger's timing.
//
// After this the variant sells from physical stock, because every read and Checkout prefer an
// ACTIVE preorder and fall back to stock when there is none. Nothing else has to change.
public sealed record ClosePreorderCommand(Guid ProductVariantId) : ICommand<PreorderResponse>;

public sealed class ClosePreorderHandler(StoreDbContext db)
    : IRequestHandler<ClosePreorderCommand, Result<PreorderResponse>>
{
    public async Task<Result<PreorderResponse>> Handle(ClosePreorderCommand request, CancellationToken ct)
    {
        var preorder = await db.Set<Preorder>()
            .FirstOrDefaultAsync(p => p.ProductVariantId == request.ProductVariantId, ct);
        if (preorder is null) return PreorderErrors.NotFound(request.ProductVariantId);

        var result = preorder.Close();
        if (result.IsFailure) return result.Error;

        // Capacity left unsold is not converted to anything: what the variant sells from now on is
        // whatever stock the store actually loaded. A drop's capacity was always a promise about
        // units that had not arrived yet, and closing it is exactly the moment that stops being
        // the right number to show.
        return ConfigurePreorderHandler.Map(preorder);
    }
}
