using System.Text;
using System.Text.Json;
using ATL;
using OsuParsers.Database;
using OsuParsers.Database.Objects;
using OsuParsers.Decoders;
using OsuParsers.Enums;
using Renako.BeatmapConverter.OsuMania;
using Renako.Game.Beatmaps;
using Renako.Game.Utilities;

// %APPDATA%\Renako\beatmaps
string renakoBeatmapsFolderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Renako", "beatmaps");
OsuDatabase osuDb = DatabaseDecoder.DecodeOsu(OsuStableLocation.DefaultDatabasePath);
JsonSerializerOptions jsonSerializerOptions = new JsonSerializerOptions()
{
    WriteIndented = true,
    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
};

void ConvertBeatmap(int beatmapSetId)
{
    // Create new Renako BeatmapSet and Beatmap object
    List<DbBeatmap> allOsuBeatmap = osuDb.Beatmaps.FindAll(b => b.BeatmapSetId == beatmapSetId);
    allOsuBeatmap.RemoveAll(b => b.Ruleset != Ruleset.Mania);
    if (allOsuBeatmap.Count == 0)
    {
        Console.WriteLine("Beatmapset doesn't have any mania beatmap.");
        return;
    }
    DbBeatmap firstBeatmap = allOsuBeatmap[0];

    Track beatmapTrack = new Track(Path.Combine(OsuStableLocation.DefaultSongsPath, firstBeatmap.FolderName, firstBeatmap.AudioFileName));

    // Calculate BPM\
    double maxBpm = firstBeatmap.TimingPoints.Max(t => t.BPM);
    int bpm = (int)(1 / maxBpm * 1000 * 60);

    // Check is beatmap have video

    OsuParsers.Beatmaps.Beatmap osuBeatmap = BeatmapDecoder.Decode(Path.Combine(OsuStableLocation.DefaultSongsPath, firstBeatmap.FolderName, firstBeatmap.FileName));

    BeatmapSet beatmapSet = new BeatmapSet()
    {
        ID = beatmapSetId,
        Title = firstBeatmap.Title,
        TitleUnicode = firstBeatmap.TitleUnicode,
        Artist = firstBeatmap.Artist,
        ArtistUnicode = firstBeatmap.ArtistUnicode,
        Source = firstBeatmap.Source,
        SourceUnicode = firstBeatmap.Source,
        TotalLength = (int)beatmapTrack.DurationMs,
        PreviewTime = firstBeatmap.AudioPreviewTime,
        BPM = bpm,
        Creator = firstBeatmap.Creator,
        HasVideo = !string.IsNullOrEmpty(osuBeatmap.EventsSection.Video),
        UseLocalSource = false,
        CoverPath = osuBeatmap.EventsSection.BackgroundImage,
        TrackPath = firstBeatmap.AudioFileName,
        BackgroundPath = osuBeatmap.EventsSection.BackgroundImage,
        VideoPath = osuBeatmap.EventsSection.Video
    };

    List<Beatmap> allRenakoBeatmaps = new List<Beatmap>();

    for (int i = 0; i < allOsuBeatmap.Count; i++)
    {
        DbBeatmap beatmap = allOsuBeatmap[i];
        // Check circle size for mania column
        if (Math.Abs(beatmap.CircleSize - 4) > 0.1)
        {
            Console.WriteLine($"Beatmap {beatmap.BeatmapId} has circle size {beatmap.CircleSize}, skipping.");
            continue;
        }
        Beatmap renakoBeatmap = new Beatmap()
        {
            ID = beatmap.BeatmapId,
            BeatmapSet = beatmapSet,
            Creator = beatmap.Creator,
            DifficultyName = beatmap.Difficulty,
            DifficultyRating = beatmap.ManiaStarRating.TryGetValue(Mods.None, out double starRating) ? starRating : 0,
            BackgroundPath = beatmapSet.BackgroundPath
        };
        OsuParsers.Beatmaps.Beatmap fullBeatmap = BeatmapDecoder.Decode(Path.Combine(OsuStableLocation.DefaultSongsPath, beatmap.FolderName, beatmap.FileName));
        List<BeatmapNote> notes = new List<BeatmapNote>();
        foreach (var hitObject in fullBeatmap.HitObjects)
        {
            if (hitObject is OsuParsers.Beatmaps.Objects.Mania.ManiaNote note)
            {
                notes.Add(new BeatmapNote()
                {
                    Lane = (NoteLane)note.GetColumn((int)beatmap.CircleSize),
                    StartTime = note.StartTime,
                    EndTime = note.EndTime,
                    // Renako still doesn't support hold note
                    Type = NoteType.BasicNote
                });
            } else if (hitObject is OsuParsers.Beatmaps.Objects.Mania.ManiaHoldNote holdNote)
            {
                notes.Add(new BeatmapNote()
                {
                    // TODO: Implement this when Renako support hold note
                    Lane = (NoteLane)holdNote.GetColumn((int)beatmap.CircleSize),
                    StartTime = holdNote.StartTime,
                    EndTime = holdNote.StartTime,
                    Type = NoteType.BasicNote
                });
            }
        }
        renakoBeatmap.Notes = notes.ToArray();
        allRenakoBeatmaps.Add(renakoBeatmap);
    }

    // Start writing Renako beatmap

    try
    {
        // Create beatmapset folder
        string beatmapSetFolderPath = Path.Combine(renakoBeatmapsFolderPath, BeatmapSetUtility.GetFolderName(beatmapSet));
        if (Directory.Exists(beatmapSetFolderPath))
        {
            Directory.Delete(beatmapSetFolderPath, true);
        }
        Directory.CreateDirectory(beatmapSetFolderPath);

        // Write beatmapset file
        string beatmapSetFilePath = Path.Combine(beatmapSetFolderPath, BeatmapSetUtility.GetBeatmapSetFileName(beatmapSet) + ".rks");
        using (FileStream stream = File.Create(beatmapSetFilePath))
        {
            string json = JsonSerializer.Serialize(beatmapSet, jsonSerializerOptions);
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            stream.Write(bytes);
        }

        // Write beatmap files
        foreach (var beatmap in allRenakoBeatmaps)
        {
            string beatmapFilePath = Path.Combine(beatmapSetFolderPath, BeatmapUtility.GetBeatmapFileName(beatmap) + ".rkb");
            using (FileStream stream = File.Create(beatmapFilePath))
            {
                string json = JsonSerializer.Serialize(beatmap, jsonSerializerOptions);
                byte[] bytes = Encoding.UTF8.GetBytes(json);
                stream.Write(bytes);
            }
        }

        // Copy necessary files
        File.Copy(Path.Combine(OsuStableLocation.DefaultSongsPath, firstBeatmap.FolderName, firstBeatmap.AudioFileName), Path.Combine(beatmapSetFolderPath, firstBeatmap.AudioFileName), true);
        File.Copy(Path.Combine(OsuStableLocation.DefaultSongsPath, firstBeatmap.FolderName, osuBeatmap.EventsSection.BackgroundImage), Path.Combine(beatmapSetFolderPath, osuBeatmap.EventsSection.BackgroundImage), true);
        if (!string.IsNullOrEmpty(osuBeatmap.EventsSection.Video))
        {
            File.Copy(Path.Combine(OsuStableLocation.DefaultSongsPath, firstBeatmap.FolderName, osuBeatmap.EventsSection.Video), Path.Combine(beatmapSetFolderPath, osuBeatmap.EventsSection.Video), true);
        }
    } catch (Exception e)
    {
        Console.WriteLine("Failed to convert beatmapset: " + e.Message + "\n" + e.StackTrace);
        Console.WriteLine("Try delete the beatmapset folder if already created.");
        string beatmapSetFolderPath = Path.Combine(renakoBeatmapsFolderPath, BeatmapSetUtility.GetFolderName(beatmapSet));
        if (Directory.Exists(beatmapSetFolderPath))
        {
            Directory.Delete(beatmapSetFolderPath, true);
            Console.WriteLine("Beatmapset folder deleted.");
        }
    }
}

List<string> allBeatmapSet = new List<string>();
List<int> allBeatmapSetId = new List<int>();

foreach (var beatmap in osuDb.Beatmaps.FindAll(b => b.Ruleset == Ruleset.Mania))
{
    string easyToRead = $"{beatmap.BeatmapSetId} {beatmap.Artist} - {beatmap.Title}";
    if (!allBeatmapSet.Contains(easyToRead))
    {
        allBeatmapSet.Add(easyToRead);
        allBeatmapSetId.Add(beatmap.BeatmapSetId);
    }
}

Console.WriteLine("All beatmapset count: " + allBeatmapSet.Count);
Console.WriteLine("All beatmap count: " + osuDb.Beatmaps.Count);
Console.WriteLine("All mania beatmap count: " + osuDb.Beatmaps.Count(b => b.Ruleset == Ruleset.Mania));
Console.WriteLine("All beatmapset count after removing non-mania beatmap: " + allBeatmapSet.Count);

Console.WriteLine("All beatmapset:");

for (int i = 0; i < allBeatmapSet.Count; i++)
{
    Console.WriteLine($"{i + 1}. {allBeatmapSet[i]}");
}

Console.Write("Enter the beatmapset id to convert (or write 'all' to convert all beatmapset): ");
if (Console.ReadLine() == "all")
{
    foreach (int beatmapSetId in allBeatmapSetId)
    {
        Console.WriteLine($"Converting beatmapset {beatmapSetId}...");
        ConvertBeatmap(beatmapSetId);
    }
}
else
{
    int beatmapSetId = int.Parse(Console.ReadLine() ?? string.Empty);
    if (!allBeatmapSetId.Contains(beatmapSetId))
    {
        Console.WriteLine("Invalid beatmapset id.");
    }
}
