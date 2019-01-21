# DeviantArtFs.Stash.Marshal

An F# library (.NET Standard 2.0) to interact with the [Sta.sh API.](https://www.deviantart.com/developers/http/v1/20160316)

This library sits on top of DeviantArtFs and provides a StashRoot object that can be used to process reponses from the Sta.sh delta endpoint.

The delta response contains a list of entries that need to be applied in order. The interface IStashDelta is used to represent these endpoints:

	namespace DeviantArtFs {
		public interface IStashDelta {
			long? Itemid { get; }
			long? Stackid { get; }
			string MetadataJson { get; }
			int? Position { get; }
		}
	}

IStashDelta is implemented both by DeviantArtFs.StashDeltaEntry, the type used for responses from the server,
and by SerializedDeltaEntry, the type that StashRoot uses for export.
You can also implement it yourself (e.g. on an object representing a database row.)

Example usage (C#):

	string cursor = null;
	var stashRoot = new StashRoot();

	void Refresh() {
		var delta = await DeviantArt.Requests.Stash.Delta.GetAllAsync(token, new DeviantArt.Requests.Stash.DeltaAllRequest { Cursor = StashCursor });
		cursor = delta.Cursor;

		Deserialize(delta.Entries);
	}

	List<SerializedDeltaEntry> Serialize() {
		return stashRoot.Save();
	}

	void Deserialize(IEnumerable<IStashDelta> list) {
		stashRoot.Clear();
        foreach (var x in list) {
            stashRoot.Apply(x);
        }
	}

See the project ExampleWebApp for a more concrete example.

Known bugs:

* When editing a Sta.sh item from the DeviantArt submission page (and using "Save & Exit"), its itemid will change, and StashRoot will think it's a different item. This seems to be a bug on DeviantArt's end.
