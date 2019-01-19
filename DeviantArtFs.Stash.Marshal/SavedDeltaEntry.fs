namespace DeviantArtFs.Stash.Marshal

open System
open DeviantArtFs

type SavedDeltaEntry =
    {
        Itemid: Nullable<int64>
        Stackid: int64
        MetadataJson: string
        Position: int
    }
    interface DeviantArtFs.IStashDelta with
        member this.Itemid = this.Itemid
        member this.Stackid = this.Stackid |> Nullable
        member this.MetadataJson = this.MetadataJson
        member this.Position = this.Position |> Nullable