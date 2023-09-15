namespace ChunkMergeTool
{
    using System;
    using System.Diagnostics;
    using System.Globalization;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    internal class Program
    {
        static readonly string WorkingDir = @"C:\Users\Fred\Documents\Git\s3unlocked\Levels\LBZ\Chunks";
        static readonly string FileLayoutAct1 = @"..\Layout\1.bin";
        static readonly string FileLayoutAct2 = @"..\Layout\2.bin";
        static readonly string FileSolidsAct1 = @"..\Collision\1.bin";
        static readonly string FileSolidsAct2 = @"..\Collision\2.bin";

        static readonly string FileChunksAct1 = "Act 1";
        static readonly string FileChunksAct2 = "Act 2";
        static readonly string FileChunksReport = @"chunks.txt";
        static readonly string FileBlocksCommon = @"..\Blocks\Primary";
        static readonly string FileBlocksAct1 = @"..\Blocks\Act 1 Secondary";
        static readonly string FileBlocksAct2 = @"..\Blocks\Act 2 Secondary";
        static readonly string FileBlocksReport = @"blocks.txt";
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

            var blockMappings = AnalyzeChunks(chunksAct1, chunksAct2, blocksCommon.Count);
            if (blockMappings == null)
            {
                Console.WriteLine("Completed with errors; a report has been created.");
                return;
            }

            var blockConfirm = AnalyzeBlocks(blockMappings);
            if (blockConfirm == null)
            {
                Console.WriteLine("Completed with errors; a report has been created.");
                return;
            }

            if (!AnalyzeTiles(blockConfirm, blocksAct1, blocksAct2, tilesAct1, tilesAct2))
            {
                Console.WriteLine("Tile mismatch error");
                return;
            }
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

        static IList<BlockMapping?>? AnalyzeChunks(IList<ChunkInfo> chunksAct1, IList<ChunkInfo> chunksAct2, int blocksCommonCount)
        {
            var chunkIgnore = new Dictionary<int, IList<int>?>();
            var chunkConfirm = new List<(int, int)>();
            var path = Path.Combine(WorkingDir, FileChunksReport);

            if (File.Exists(path) && File.ReadAllText(path) is { Length: > 0 } text)
            {
                var report = JsonSerializer.Deserialize<ChunkReport>(text)!;
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
                var report = new ChunkReport(chunksAct1, chunksAct2, chunkIgnore);

                using var file = File.CreateText(path);
                file.Write(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));

                return null;
            }

            return blockMappings;
        }

        static IList<BlockConfirmMatch>? AnalyzeBlocks(IList<BlockMapping?> blockMappings)
        {
            var blockConfirm = new List<BlockConfirmMatch>();

            for (var index = 0; index < blockMappings.Count; index++)
            {
                var mapping = blockMappings[index];
                if (mapping != null && !mapping.Common)
                    blockConfirm.Add(new BlockConfirmMatch(index, mapping));
            }

            var path = Path.Combine(WorkingDir, FileBlocksReport);

            if (File.Exists(path) && File.ReadAllText(path) is { Length: > 0 } text)
            {
                var report = JsonSerializer.Deserialize<BlockReport>(text)!;
                foreach (var confirm in report.ConfirmMatches)
                {
                    if (blockConfirm.FirstOrDefault(match => match.Block1 == confirm.Block1
                        && match.Block2 == confirm.Block2) is BlockConfirmMatch match)
                    {
                        match.XFlip = confirm.XFlip;
                        match.YFlip = confirm.YFlip;
                        match.MatchType = MatchType.Confirmed;
                    }
                }
            }

            if (blockConfirm.Any(match => match.MatchType != MatchType.Confirmed))
            {
                var report = new BlockReport(blockConfirm);

                using var file = File.CreateText(path);
                file.Write(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));

                return null;
            }

            return blockConfirm;
        }

        static bool AnalyzeTiles(IList<BlockConfirmMatch> blockConfirm, IList<BlockInfo> blocksAct1, IList<BlockInfo> blocksAct2, IList<IList<byte>> tilesAct1, IList<IList<byte>> tilesAct2)
        {
            foreach (var match in blockConfirm)
            {
                var block1 = blocksAct1[match.Block1];
                var block2 = blocksAct2[match.Block2];

                if (!match.XFlip && !match.YFlip)
                {
                    if (!CompareTiles(block1.Definition[0], block2.Definition[0], tilesAct1, tilesAct2, false, false) ||
                        !CompareTiles(block1.Definition[1], block2.Definition[1], tilesAct1, tilesAct2, false, false) ||
                        !CompareTiles(block1.Definition[2], block2.Definition[2], tilesAct1, tilesAct2, false, false) ||
                        !CompareTiles(block1.Definition[3], block2.Definition[3], tilesAct1, tilesAct2, false, false))
                        return false;
                }
                else if (match.XFlip && !match.YFlip)
                {
                    if (!CompareTiles(block1.Definition[0], block2.Definition[1], tilesAct1, tilesAct2, true, false) ||
                        !CompareTiles(block1.Definition[1], block2.Definition[0], tilesAct1, tilesAct2, true, false) ||
                        !CompareTiles(block1.Definition[2], block2.Definition[3], tilesAct1, tilesAct2, true, false) ||
                        !CompareTiles(block1.Definition[3], block2.Definition[2], tilesAct1, tilesAct2, true, false))
                        return false;
                }
                else if (!match.XFlip && match.YFlip)
                {
                    if (!CompareTiles(block1.Definition[0], block2.Definition[2], tilesAct1, tilesAct2, false, true) ||
                        !CompareTiles(block1.Definition[1], block2.Definition[3], tilesAct1, tilesAct2, false, true) ||
                        !CompareTiles(block1.Definition[2], block2.Definition[0], tilesAct1, tilesAct2, false, true) ||
                        !CompareTiles(block1.Definition[3], block2.Definition[1], tilesAct1, tilesAct2, false, true))
                        return false;
                }
                else
                {
                    if (!CompareTiles(block1.Definition[0], block2.Definition[3], tilesAct1, tilesAct2, true, true) ||
                        !CompareTiles(block1.Definition[1], block2.Definition[2], tilesAct1, tilesAct2, true, true) ||
                        !CompareTiles(block1.Definition[2], block2.Definition[1], tilesAct1, tilesAct2, true, true) ||
                        !CompareTiles(block1.Definition[3], block2.Definition[0], tilesAct1, tilesAct2, true, true))
                        return false;
                }
            }

            return true;
        }

        static bool CompareTiles(TileRef tileAct1, TileRef tileAct2, IList<IList<byte>> tilesAct1, IList<IList<byte>> tilesAct2, bool xFlip, bool yFlip)
        {
            var effectiveXFlip = xFlip ^ tileAct1.XFlip ^ tileAct2.XFlip;
            var effectiveYFlip = yFlip ^ tileAct1.YFlip ^ tileAct2.YFlip;
            IList<int> lookup;

            if (!effectiveXFlip && !effectiveYFlip) lookup = new[]
            {
                0x00, 0x01, 0x02, 0x03,
                0x04, 0x05, 0x06, 0x07,
                0x08, 0x09, 0x0A, 0x0B,
                0x0C, 0x0D, 0x0E, 0x0F,
                0x10, 0x11, 0x12, 0x13,
                0x14, 0x15, 0x16, 0x17,
                0x18, 0x19, 0x1A, 0x1B,
                0x1C, 0x1D, 0x1E, 0x1F,
            };
            else if (effectiveXFlip && !effectiveYFlip) lookup = new[]
            {
                0x03, 0x02, 0x01, 0x00, 
                0x07, 0x06, 0x05, 0x04, 
                0x0B, 0x0A, 0x09, 0x08, 
                0x0F, 0x0E, 0x0D, 0x0C, 
                0x13, 0x12, 0x11, 0x10, 
                0x17, 0x16, 0x15, 0x14, 
                0x1B, 0x1A, 0x19, 0x18, 
                0x1F, 0x1E, 0x1D, 0x1C,
            };
            else if (!effectiveXFlip && effectiveYFlip) lookup = new[]
            {
                0x1C, 0x1D, 0x1E, 0x1F,
                0x18, 0x19, 0x1A, 0x1B,
                0x14, 0x15, 0x16, 0x17,
                0x10, 0x11, 0x12, 0x13,
                0x0C, 0x0D, 0x0E, 0x0F,
                0x08, 0x09, 0x0A, 0x0B,
                0x04, 0x05, 0x06, 0x07,
                0x00, 0x01, 0x02, 0x03,
            };
            else lookup = new[]
            {
                0x1F, 0x1E, 0x1D, 0x1C,
                0x1B, 0x1A, 0x19, 0x18,
                0x17, 0x16, 0x15, 0x14,
                0x13, 0x12, 0x11, 0x10,
                0x0F, 0x0E, 0x0D, 0x0C,
                0x0B, 0x0A, 0x09, 0x08,
                0x07, 0x06, 0x05, 0x04,
                0x03, 0x02, 0x01, 0x00,
            };

            var tile1 = tilesAct1[tileAct1.Id];
            var tile2 = tilesAct2[tileAct2.Id];

            for (var index = 0; index < 0x20; index++)
            {
                var byte1 = tile1[index];
                var byte2 = tile2[lookup[index]];

                if (effectiveXFlip)
                    byte2 = (byte)(((byte2 & 0x0F) << 4) | ((byte2 & 0xF0) >> 4));

                if (byte1 != byte2)
                    return false;
            }

            return true;
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
        public IList<TileRef> Definition { get; set; }

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

    internal class BlockReport
    {
        public IList<BlockConfirmMatch> ConfirmMatches { get; set; }

        public BlockReport(IList<BlockConfirmMatch> blockConfirm)
        {
            ConfirmMatches = blockConfirm;
        }

#pragma warning disable CS8618
        public BlockReport()
        {
        }
#pragma warning restore CS8618
    }

    internal class BlockConfirmMatch
    {
        public string BlockAct1
        {
            get => Block1.ToString("X");
            set => Block1 = int.Parse(value, NumberStyles.HexNumber);
        }

        public string BlockAct2
        {
            get => Block2.ToString("X");
            set => Block2 = int.Parse(value, NumberStyles.HexNumber);
        }

        [JsonIgnore]
        public int Block1 { get; set; }

        [JsonIgnore]
        public int Block2 { get; set; }

        [JsonIgnore]
        public BlockMapping Mapping { get; set; }

        [JsonIgnore]
        public MatchType MatchType { get; set; }

        public bool XFlip { get; set; }

        public bool YFlip { get; set; }

        public BlockConfirmMatch(int id, BlockMapping mapping)
        {
            Block1 = id;
            Block2 = mapping.Id;
            Mapping = mapping;
            MatchType = MatchType.Pending;
        }

#pragma warning disable CS8618
        public BlockConfirmMatch()
        {
        }
#pragma warning restore CS8618
    }

    internal class ChunkReport
    {
        public IList<IList<string>> ConfirmMatches { get; set; }

        public IList<IList<string>> DuplicatesAct1 { get; set; }

        public IList<IList<string>> DuplicatesAct2 { get; set; }

        public IList<ChunkIgnoreMatch> IgnoreMatches { get; set; }

        public ChunkReport(IList<ChunkInfo> chunksAct1, IList<ChunkInfo> chunksAct2, Dictionary<int, IList<int>?> chunkIgnore)
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
            IgnoreMatches = new List<ChunkIgnoreMatch>();

            if (!chunkIgnore.Any())
                IgnoreMatches.Add(new ChunkIgnoreMatch(0, new List<int> { 0 }));

            else foreach (var index1 in chunkIgnore.Keys)
            {
                var ignore = chunkIgnore[index1];
                IgnoreMatches.Add(new ChunkIgnoreMatch(index1, ignore));
            }
        }

#pragma warning disable CS8618
        public ChunkReport()
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

    internal class ChunkIgnoreMatch
    {
        public string Chunk1 { get; set; }

        public IList<string>? Chunk2 { get; set; }

        public ChunkIgnoreMatch(int index1, IList<int>? ignore)
        {
            Chunk1 = index1.ToString("X");
            Chunk2 = ignore?.Select(index2 => index2.ToString("X")).ToList();
        }

#pragma warning disable CS8618
        public ChunkIgnoreMatch()
        {
        }
#pragma warning restore CS8618
    }

}