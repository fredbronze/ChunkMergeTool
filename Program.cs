namespace ChunkMergeTool
{
    using System;
    using System.Diagnostics;
    using System.Globalization;
    using System.Text;
    using System.Text.Json;

    internal class Program
    {
        static readonly string WorkingDir = @"C:\Users\Fred\Documents\Git\s3unlocked\Levels\LBZ\Chunks";
        static readonly string FileReport = @"report.txt";
        static readonly string FileLayoutAct1 = @"..\Layout\1.bin";
        static readonly string FileLayoutAct2 = @"..\Layout\2.bin";
        static readonly string FileSolidsAct1 = @"..\Collision\1.bin";
        static readonly string FileSolidsAct2 = @"..\Collision\2.bin";

        static readonly string FileChunksAct1 = "Act 1";
        static readonly string FileChunksAct2 = "Act 2";
        static readonly string FileBlocksCommon = @"..\Blocks\Primary";
        static readonly string FileBlocksAct1 = @"..\Blocks\Act 1 Secondary";
        static readonly string FileBlocksAct2 = @"..\Blocks\Act 2 Secondary";
        static readonly string FileTilesCommon = @"..\Tiles\Primary";
        static readonly string FileTilesAct1 = @"..\Tiles\Act 1 Secondary";
        static readonly string FileTilesAct2 = @"..\Tiles\Act 2 Secondary";

        static void Main()
        {
            var layoutAct1 = ReadLayout(FileLayoutAct1);
            var layoutAct2 = ReadLayout(FileLayoutAct2);
            var chunksAct1 = ReadChunks(FileChunksAct1);
            var chunksAct2 = ReadChunks(FileChunksAct2);

            var blocksCommon = ReadBlocks(FileBlocksCommon);
            var blocksAct1 = blocksCommon.Concat(ReadBlocks(FileBlocksAct1)).ToList();
            var blocksAct2 = blocksCommon.Concat(ReadBlocks(FileBlocksAct2)).ToList();
            ReadSolids(FileSolidsAct1, blocksAct1);
            ReadSolids(FileSolidsAct2, blocksAct2);

            var tilesCommon = ReadTiles(FileTilesCommon);
            var tilesAct1 = tilesCommon.Concat(ReadTiles(FileTilesAct1)).ToList();
            var tilesAct2 = tilesCommon.Concat(ReadTiles(FileTilesAct2)).ToList();

            chunksAct1[0xDA].Used = true;
            chunksAct2[0xA6].Used = true;
            chunksAct2[0xA7].Used = true;
            MarkUsedChunks(chunksAct1, layoutAct1);
            MarkUsedChunks(chunksAct2, layoutAct2);

            MarkDuplicateChunks(chunksAct1);
            MarkDuplicateChunks(chunksAct2);
            BlankUnusedChunks(chunksAct1);
            BlankUnusedChunks(chunksAct2);

            var blockMappings = Analyze(chunksAct1, chunksAct2, blocksCommon.Count);
            var blockMatches = blockMappings?.Select((mapping, index) => new
            {
                BlockAct1 = index,
                BlockAct2 = mapping?.Id ?? -1,
                Mapping = mapping
            })
            .Where(block => block.BlockAct1 >= blocksCommon.Count && block.BlockAct2 >= 0)
            .ToList();
        }

        static void MarkUsedChunks(IList<ChunkInfo> chunks, LayoutInfo layout)
        {
            foreach (var row in layout.Rows)
                foreach (var chunk in row.Chunks)
                    chunks[chunk].Used = true;
        }

        static void MarkDuplicateChunks(IList<ChunkInfo> chunks)
        {
            for (var index1 = 0; index1 < chunks.Count; index1++)
            {
                var chunk1 = chunks[index1];
                if (chunk1.MatchType == MatchType.Duplicate) continue;

                for (var index2 = 0; index2 < chunks.Count; index2++)
                {
                    if (index1 == index2) continue;
                    var chunk2 = chunks[index2];
                    var match = true;

                    for (var blockIndex = 0; blockIndex < 0x40; blockIndex++)
                    {
                        if (chunk1.Definition[blockIndex].Word != chunk2.Definition[blockIndex].Word)
                        {
                            match = false;
                            break;
                        }
                    }

                    if (match)
                    {
                        chunk2.MatchType = MatchType.Duplicate;
                        chunk2.Match = (byte)index1;
                    }
                }
            }
        }

        static void BlankUnusedChunks(IList<ChunkInfo> chunks)
        {
            var blankDefinition = chunks[0].Definition;
            foreach (var chunk in chunks.Where(chunk => !chunk.Used))
                chunk.Definition = blankDefinition;
        }

        static IList<BlockMapping?>? Analyze(IList<ChunkInfo> chunksAct1, IList<ChunkInfo> chunksAct2, int blocksCommonCount)
        {
            var chunkIgnore = new Dictionary<int, IList<int>?>();
            var chunkConfirm = new List<(int, int)>();
            var path = Path.Combine(WorkingDir, FileReport);

            if (File.Exists(path) && File.ReadAllText(path) is { Length: > 0 } text)
            {
                var report = JsonSerializer.Deserialize<AnalysisReport>(text)!;
                foreach (var ignore in report.IgnoreMatches)
                {
                    var index1 = int.Parse(ignore.Chunk1, NumberStyles.HexNumber);
                    var index2 = ignore.Chunk2?.Select(index => int.Parse(index, NumberStyles.HexNumber)).ToList();
                    chunkIgnore.Add(index1, index2);
                }

                foreach (var confirm in report.ConfirmMatches)
                {
                    var index1 = int.Parse(confirm[0], NumberStyles.HexNumber);
                    var index2 = int.Parse(confirm[1], NumberStyles.HexNumber);
                    chunkConfirm.Add((index1, index2));
                }
            }

            var blockMappings = new List<BlockMapping?>(0x300);
            for (var index = 0; index < blocksCommonCount; index++)
                blockMappings.Add(new BlockMapping(index));
            for (var index = blockMappings.Count; index < 0x300; index++)
                blockMappings.Add(null);

            var errors = false;

            for (var index1 = 0; index1 < chunksAct1.Count; index1++)
            {
                if (chunkIgnore.TryGetValue(index1, out var ignore) && ignore == null)
                    continue;

                var chunk1 = chunksAct1[index1];
                if (!chunk1.Used || chunk1.MatchType != MatchType.Unique) continue;

                for (int index2 = 1; index2 < chunksAct2.Count; index2++)
                {
                    if (ignore != null && ignore.Contains(index2))
                        continue;

                    var chunk2 = chunksAct2[index2];
                    if (!chunk2.Used) continue;

                    var guessedMappings = new Dictionary<int, int>();
                    var match = true;

                    for (var blockIndex = 0; blockIndex < 0x40; blockIndex++)
                    {
                        var block1 = chunk1.Definition[blockIndex];
                        var block2 = chunk2.Definition[blockIndex];

                        if (block1.Id < blocksCommonCount)
                        {
                            if (block1.Id == block2.Id)
                                continue;

                            match = false;
                            break;
                        }

                        if (!guessedMappings.TryGetValue(block1.Id, out var guessedBlock2))
                        {
                            guessedMappings.Add(block1.Id, block2.Id);
                        }
                        else if (block2.Id != guessedBlock2)
                        {
                            match = false;
                            break;
                        }
                    }

                    if (!match)
                        continue;

                    for (var blockIndex = 0; blockIndex < 0x40; blockIndex++)
                    {
                        var block1 = chunk1.Definition[blockIndex];
                        var block2 = chunk2.Definition[blockIndex];
                        var expected = blockMappings[block1.Id];

                        if (expected == null || expected.Id == block2.Id)
                            continue;

                        Console.WriteLine(
                            $"Act 1 chunk: {index1:X}\r\n" +
                            $"Act 2 chunk: {index2:X}\r\n" +
                            $"Act 1 block: {block1.Id:X}\r\n" +
                            $"Act 2 block: {block2.Id:X}\r\n" +
                            $"Expected block: {expected.Id:X}\r\n" +
                            (expected.Common ? "Block is part of primary set (shouldn't happen)" :
                            $"Guess produced while mapping chunk {expected.Chunk1:X} to {expected.Chunk2:X}")
                        );

                        errors = true;
                    }

                    chunk1.MatchType = MatchType.Pending;
                    chunk1.Match = (byte)index2;

                    if (chunkConfirm.Any(match => match.Item1 == index1 && match.Item2 == index2))
                        chunk1.MatchType = MatchType.Confirmed;

                    foreach (var m in guessedMappings)
                        blockMappings[m.Key] = new BlockMapping(m.Value, (byte)index1, (byte)index2);

                    break;
                }

                if (chunk1.MatchType != MatchType.Unique)
                    continue;
            }

            var pendingAct1 = chunksAct1.Any(chunk => chunk.MatchType == MatchType.Pending);
            var pendingAct2 = chunksAct2.Any(chunk => chunk.MatchType == MatchType.Pending);

            if (pendingAct1 || pendingAct2 || errors)
            {
                var report = new AnalysisReport(chunksAct1, chunksAct2, chunkIgnore);

                using var file = File.CreateText(path);
                file.Write(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));

                Console.WriteLine("Completed with errors; a report has been created.");
                return null;
            }

            return blockMappings;
        }

        static IList<IList<byte>> ReadTiles(string filename)
        {
            var compressed = $"{filename}.bin";
            var uncompressed = $"{filename} unc.bin";
            ProcessKosFile(compressed, uncompressed, moduled: true, extract: true);

            Thread.Sleep(3000);

            var file = File.OpenRead(Path.Combine(WorkingDir, uncompressed));
            var list = new List<IList<byte>>();

            while (file.Position != file.Length)
            {
                var bytes = new byte[0x20];
                file.Read(bytes);
                list.Add(bytes);
            }

            return list;
        }

        static void ReadSolids(string filename, IList<BlockInfo> blocks)
        {
            var file = File.OpenRead(Path.Combine(WorkingDir, filename));

            foreach (var block in blocks)
                block.Solidity = ReadWord(file);
        }

        static IList<BlockInfo> ReadBlocks(string filename)
        {
            var compressed = $"{filename}.bin";
            var uncompressed = $"{filename} unc.bin";
            ProcessKosFile(compressed, uncompressed, moduled: false, extract: true);

            Thread.Sleep(3000);

            var file = File.OpenRead(Path.Combine(WorkingDir, uncompressed));
            var list = new List<BlockInfo>();

            while (file.Position != file.Length)
            {
                var definition = new List<TileRef>(4);
                for (var index = 0; index < 4; index++)
                {
                    var word = ReadWord(file);
                    definition.Add(new TileRef(word));
                }

                list.Add(new BlockInfo(definition));
            }

            return list;
        }

        static IList<ChunkInfo> ReadChunks(string filename)
        {
            var compressed = $"{filename}.bin";
            var uncompressed = $"{filename} unc.bin";
            ProcessKosFile(compressed, uncompressed, moduled: false, extract: true);

            Thread.Sleep(3000);

            var file = File.OpenRead(Path.Combine(WorkingDir, uncompressed));
            var list = new List<ChunkInfo>();

            while (file.Position != file.Length)
            {
                var definition = new List<BlockRef>(0x40);
                for (var index = 0; index < 0x40; index++)
                {
                    var word = ReadWord(file);
                    definition.Add(new BlockRef(word));
                }

                list.Add(new ChunkInfo(definition));
            }

            return list;
        }

        static LayoutInfo ReadLayout(string filename)
        {
            var file = File.OpenRead(Path.Combine(WorkingDir, filename));
            var widthFG = ReadWord(file);
            var widthBG = ReadWord(file);
            var heightFG = ReadWord(file);
            var heightBG = ReadWord(file);
            var ptrsFG = new List<int>(0x20);
            var ptrsBG = new List<int>(0x20);

            for (var index = 0; index < 0x20; index++)
            {
                ptrsFG.Add(ReadWord(file));
                ptrsBG.Add(ReadWord(file));
            }

            var foreground = ReadLayoutRows(file, widthFG, heightFG, ptrsFG);
            var background = ReadLayoutRows(file, widthBG, heightBG, ptrsBG);
            return new LayoutInfo(foreground, background);
        }

        static IList<LayoutRow> ReadLayoutRows(FileStream file, int bufferSize, int rowCount, IEnumerable<int> rowPtrs)
        {
            var rows = new List<LayoutRow>(rowCount);

            foreach (var ptr in rowPtrs)
            {
                if (rows.Count == rowCount)
                    break;

                var buffer = new byte[bufferSize];
                file.Seek(ptr - 0x8000, SeekOrigin.Begin);
                file.Read(buffer);
                rows.Add(new LayoutRow(buffer));
            }

            return rows;
        }

        static int ReadWord(FileStream file)
        {
            return (file.ReadByte() << 8) | file.ReadByte();
        }

        static void ProcessKosFile(string source, string destination, bool moduled, bool extract)
        {
            var args = new StringBuilder();

            if (extract) { args.Append("-x "); }
            if (moduled) { args.Append("-m "); }

            args.Append('"');
            args.Append(source);
            args.Append("\" \"");
            args.Append(destination);
            args.Append('"');

            Process.Start(new ProcessStartInfo("koscmp.exe", args.ToString())
            {
                WorkingDirectory = WorkingDir,
                CreateNoWindow = true
            });
        }
    }

    internal class LayoutInfo
    {
        public IList<LayoutRow> Foreground { get; set; }

        public IList<LayoutRow> Background { get; set; }

        public LayoutInfo(IList<LayoutRow> foreground, IList<LayoutRow> background)
        {
            Foreground = foreground;
            Background = background;
        }

        public IEnumerable<LayoutRow> Rows => Foreground.Concat(Background);
    }

    internal class LayoutRow
    {
        public IList<byte> Chunks { get; set; }

        public LayoutRow(IList<byte> chunks)
        {
            Chunks = chunks;
        }
    }

    internal class ChunkInfo
    {
        public IList<BlockRef> Definition { get; set; }

        public bool Used { get; set; }

        public MatchType MatchType { get; set; }

        public byte Match { get; set; }

        public bool Confirmed { get; set; }

        public ChunkInfo(IList<BlockRef> definition)
        {
            Definition = definition;
        }

        public IEnumerable<int> Words => Definition.Select(blockRef => blockRef.Word);
    }

    internal enum MatchType
    {
        Unique,
        Duplicate,
        Pending,
        Confirmed
    }

    internal class BlockRef
    {
        public int Id { get; set; }

        public bool XFlip { get; set; }

        public bool YFlip { get; set; }

        public BlockSolidity ForegroundSolid { get; set; }

        public BlockSolidity BackgroundSolid { get; set; }

        public BlockRef(int word)
        {
            Id = word & 0x3FF;
            XFlip = (word & 0x400) != 0;
            YFlip = (word & 0x800) != 0;
            ForegroundSolid = (BlockSolidity)((word & 0x3000) >> 12);
            BackgroundSolid = (BlockSolidity)((word & 0xC000) >> 14);
        }

        public int Word =>
            (int)BackgroundSolid << 14 |
            (int)ForegroundSolid << 12 |
            (YFlip ? 0x800 : 0) |
            (XFlip ? 0x400 : 0) |
            Id;
    }

    internal enum BlockSolidity : byte
    {
        None,
        Top,
        Sides,
        All
    }

    internal class BlockInfo
    {
        IList<TileRef> Definition { get; set; }

        public int Solidity { get; set; }

        public BlockInfo(IList<TileRef> definition)
        {
            Definition = definition;
        }

        public IEnumerable<int> Words => Definition.Select(tileRef => tileRef.Word);
    }

    internal class TileRef
    {
        public int Id { get; set; }

        public bool XFlip { get; set; }

        public bool YFlip { get; set; }

        public byte Palette { get; set; }

        public bool Priority { get; set; }

        public TileRef(int word)
        {
            Id = word & 0x7FF;
            XFlip = (word & 0x800) != 0;
            YFlip = (word & 0x1000) != 0;
            Palette = (byte)((word & 0x6000) >> 13);
            Priority = (word & 0x8000) != 0;
        }

        public int Word =>
            (Priority ? 0x8000 : 0) |
            (Palette << 13) |
            (YFlip ? 0x800 : 0) |
            (XFlip ? 0x400 : 0) |
            Id;
    }

    internal class BlockMapping
    {
        public int Id { get; set; }

        public byte Chunk1 { get; set; }

        public byte Chunk2 { get; set; }

        public bool Common { get; set; }

        public BlockMapping(int id)
        {
            Id = id;
            Common = true;
        }

        public BlockMapping(int id, byte chunk1, byte chunk2)
        {
            Id = id;
            Chunk1 = chunk1;
            Chunk2 = chunk2;
        }
    }

    internal class AnalysisReport
    {
        public IList<IList<string>> ConfirmMatches { get; set; }

        public IList<IList<string>> DuplicatesAct1 { get; set; }

        public IList<IList<string>> DuplicatesAct2 { get; set; }

        public IList<IgnoreMatch> IgnoreMatches { get; set; }

        public AnalysisReport(IList<ChunkInfo> chunksAct1, IList<ChunkInfo> chunksAct2, Dictionary<int, IList<int>?> chunkIgnore)
        {
            ConfirmMatches = chunksAct1
                .Select((chunk, index) => (chunk, index))
                .Where(match => match.chunk.MatchType == MatchType.Pending)
                .Select(match => new List<string>
                {
                    match.index.ToString("X"),
                    match.chunk.Match.ToString("X")
                }
                as IList<string>)
                .ToList();
            DuplicatesAct1 = CollectDuplicates(chunksAct1);
            DuplicatesAct2 = CollectDuplicates(chunksAct2);
            IgnoreMatches = new List<IgnoreMatch>();

            if (!chunkIgnore.Any())
                IgnoreMatches.Add(new IgnoreMatch(0, new List<int> { 0 }));

            else foreach (var index1 in chunkIgnore.Keys)
            {
                var ignore = chunkIgnore[index1];
                IgnoreMatches.Add(new IgnoreMatch(index1, ignore));
            }
        }

#pragma warning disable CS8618
        public AnalysisReport()
        {
        }
#pragma warning restore CS8618

        private static IList<IList<string>> CollectDuplicates(IList<ChunkInfo> chunks)
        {
            return chunks
                .Select((chunk, index) => (chunk, index))
                .Where(match => match.chunk.MatchType == MatchType.Duplicate)
                .GroupBy(match => match.chunk.Match)
                .OrderBy(group => group.Key)
                .Select(group => group
                    .Select(chunk => chunk.index)
                    .Prepend(group.Key)
                    .Select(index => index.ToString("X"))
                    .ToList() as IList<string>)
                .ToList();
        }
    }

    internal class IgnoreMatch
    {
        public string Chunk1 { get; set; }

        public IList<string>? Chunk2 { get; set; }

        public IgnoreMatch(int index1, IList<int>? ignore)
        {
            Chunk1 = index1.ToString("X");
            Chunk2 = ignore?.Select(index2 => index2.ToString("X")).ToList();
        }

#pragma warning disable CS8618
        public IgnoreMatch()
        {
        }
#pragma warning restore CS8618
    }

}