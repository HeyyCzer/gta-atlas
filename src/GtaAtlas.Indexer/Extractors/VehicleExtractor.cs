using CodeWalker.GameFiles;
using GtaAtlas.Data;

namespace GtaAtlas.Indexer.Extractors;

public sealed class VehicleExtractor : Extractor
{
    public override bool Accepts(RpfFileEntry entry) => entry.NameLower == "vehicles.meta";

    public override IEnumerable<RawAsset> Extract(RpfFileEntry entry, GameText text)
    {
        var xml = GameArchive.ReadText(entry);
        if (xml is null)
            yield break;

        foreach (var v in VehicleMeta.Parse(xml))
        {
            var name = v.ModelName.ToLowerInvariant();
            var make = text.Get(v.MakeName) ?? (v.MakeName is null ? null : VehicleMeta.Humanize(v.MakeName, ""));
            var vehicleClass = VehicleMeta.PrettyClass(v.Class);
            var type = VehicleMeta.PrettyType(v.Type);

            yield return Raw(entry, AssetKind.Vehicle, name, Names.Joaat(name)) with
            {
                DisplayName = text.Get(v.GameName),
                Data = new
                {
                    make,
                    vehicleClass,
                    type,
                    v.TxdName,
                    v.HandlingId,
                    v.GameName,
                    v.MakeName,
                    rawClass = v.Class,
                    rawType = v.Type,
                    v.Layout,
                    v.WheelType,
                    v.Swankness,
                    v.Flags,
                    v.Kits,
                },
                Tags = new[] { make, vehicleClass, type }.OfType<string>().ToList(),
            };
        }
    }
}
