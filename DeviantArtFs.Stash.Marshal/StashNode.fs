namespace DeviantArtFs.Stash.Marshal

open DeviantArtFs

type StashNode(root: IStashRoot, metadata: StashMetadata) =
    member val Metadata = metadata with get, set

    member this.BclMetadata = this.Metadata :> IBclStashMetadata

    member internal this.ParentStackId =
        match this.Metadata.Itemid with
        | Some _ -> this.Metadata.Stackid
        | None -> this.Metadata.Parentid

    member this.Position =
        match root.Nodes |> Seq.tryFindIndex (LanguagePrimitives.PhysicalEquality this) with
        | Some p -> p
        | None -> -1

    member this.Save() = {
        Itemid = this.Metadata.Itemid |> Option.toNullable
        Stackid = this.Metadata.Stackid |> Option.get
        MetadataJson = this.Metadata.Json
        Position = this.Position
    }

    member this.Children =
        match this.Metadata.Itemid with
        | Some _ -> Seq.empty
        | None -> seq {
            for n in root.Nodes do
                if n.ParentStackId = this.Metadata.Stackid then
                    yield n
        }

    member this.Stacks =
        match this.Metadata.Itemid with
        | Some _ -> Seq.empty
        | None -> seq {
            for n in this.Children do
                match n.Metadata.Itemid with
                | Some _ -> ()
                | None -> yield n
        }

    member this.Items =
        match this.Metadata.Itemid with
        | Some _ -> Seq.empty
        | None -> seq {
            for n in this.Children do
                match n.Metadata.Itemid with
                | Some _ -> yield n
                | None -> ()
        }

    override this.ToString() = this.Metadata.Title |> Option.defaultValue "StashNode"
and IStashRoot =
    abstract member Nodes: seq<StashNode>
