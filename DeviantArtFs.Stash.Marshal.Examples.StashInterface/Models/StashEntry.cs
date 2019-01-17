using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

namespace DeviantArtFs.Stash.Marshal.Examples.StashInterface.Models
{
    public class StashEntry : ISerializedStashDeltaEntry
    {
        public int StashEntryId { get; set; }

        public Guid UserId { get; set; }

        public long? ItemId { get; set; }

        public long StackId { get; set; }

        [Required]
        public string MetadataJson { get; set; }

        public int Position { get; set; }

        long? ISerializedStashDeltaEntry.Itemid => ItemId;

        long? ISerializedStashDeltaEntry.Stackid => StackId;

        string ISerializedStashDeltaEntry.MetadataJson => MetadataJson;

        int? ISerializedStashDeltaEntry.Position => Position;
    }
}
