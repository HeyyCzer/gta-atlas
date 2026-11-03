using CodeWalker.GameFiles;
using GtaAtlas.Data;

namespace GtaAtlas.Indexer.Extractors;

public sealed class PedExtractor : Extractor
{
    public override bool Accepts(RpfFileEntry entry) => entry.NameLower is "peds.meta" or "peds.ymt" or "peds.pso.meta";

    public override IEnumerable<RawAsset> Extract(RpfFileEntry entry, GameText text)
    {
        var peds = GameArchive.Load<PedsFile>(entry);
        var items = peds?.InitDataList?.InitDatas;
        if (items is null)
            yield break;

        foreach (var p in items)
        {
            if (string.IsNullOrWhiteSpace(p.Name))
                continue;
            var name = p.Name.ToLowerInvariant();
            var pedType = string.IsNullOrEmpty(p.Pedtype) ? null : VehicleMeta.Humanize(p.Pedtype, "");

            yield return Raw(entry, AssetKind.Ped, name, Names.Joaat(name)) with
            {
                Data = new
                {
                    pedType,
                    rawPedType = p.Pedtype,
                    p.PropsName,
                    p.ClipDictionaryName,
                    p.MovementClipSet,
                    p.ExpressionSetName,
                    p.FacialClipsetGroupName,
                },
                Tags = pedType is null ? [] : [pedType],
            };
        }
    }
}
