using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Globalization;
using System.IO.Compression;
using System.Text;
namespace cli_life
{
    public class Cell
    {
        public bool IsAlive;
        public readonly List<Cell> neighbors = new List<Cell>();
        private bool IsAliveNext;

        public void DetermineNextLiveState()
        {
            int liveNeighbors = neighbors.Count(x => x.IsAlive);
            IsAliveNext = IsAlive ? liveNeighbors == 2 || liveNeighbors == 3 : liveNeighbors == 3;
        }

        public void Advance()
        {
            IsAlive = IsAliveNext;
        }
    }

    public class Settings
    {
        public int Width { get; set; } = 50;
        public int Height { get; set; } = 20;
        public int CellSize { get; set; } = 1;
        public double Density { get; set; } = 0.25;
        public int DelayMs { get; set; } = 200;
        public int Generations { get; set; } = 200;

        public static Settings Load(string path = "settings.json")
        {
            try
            {
                if (!File.Exists(path))
                {
                    var defaults = new Settings();
                    defaults.Save(path);
                    return defaults;
                }

                var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path), new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }) ?? new Settings();
                settings.Normalize();
                return settings;
            }
            catch
            {
                return new Settings();
            }
        }

        public void Save(string path = "settings.json")
        {
            Normalize();
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }

        private void Normalize()
        {
            Width = Math.Max(1, Width);
            Height = Math.Max(1, Height);
            CellSize = Math.Max(1, CellSize);
            DelayMs = Math.Max(0, DelayMs);
            Generations = Math.Max(1, Generations);
            Density = Math.Clamp(Density, 0.0, 1.0);
        }
    }

    public class Board
    {
        public readonly Cell[,] Cells;
        public readonly int CellSize;
        private readonly Random rand;

        public int Columns => Cells.GetLength(0);
        public int Rows => Cells.GetLength(1);
        public int Width => Columns * CellSize;
        public int Height => Rows * CellSize;

        public Board(int width, int height, int cellSize, double liveDensity = .1, int? seed = null)
        {
            CellSize = Math.Max(1, cellSize);
            int columns = Math.Max(1, width / CellSize);
            int rows = Math.Max(1, height / CellSize);
            Cells = new Cell[columns, rows];
            rand = seed.HasValue ? new Random(seed.Value) : new Random();

            for (int x = 0; x < Columns; x++)
                for (int y = 0; y < Rows; y++)
                    Cells[x, y] = new Cell();

            ConnectNeighbors();
            Randomize(liveDensity);
        }

        public void Randomize(double liveDensity)
        {
            liveDensity = Math.Clamp(liveDensity, 0.0, 1.0);
            foreach (var cell in Cells)
                cell.IsAlive = rand.NextDouble() < liveDensity;
        }

        public void Clear()
        {
            foreach (var cell in Cells)
                cell.IsAlive = false;
        }

        public void Advance()
        {
            foreach (var cell in Cells)
                cell.DetermineNextLiveState();
            foreach (var cell in Cells)
                cell.Advance();
        }

        public int CountAlive()
        {
            int total = 0;
            foreach (var cell in Cells)
                if (cell.IsAlive) total++;
            return total;
        }

        public List<List<(int x, int y)>> GetCombinations()
        {
            var result = new List<List<(int x, int y)>>();
            var visited = new bool[Columns, Rows];
            int[] dx = { -1, 0, 1, -1, 1, -1, 0, 1 };
            int[] dy = { -1, -1, -1, 0, 0, 1, 1, 1 };

            for (int x = 0; x < Columns; x++)
            {
                for (int y = 0; y < Rows; y++)
                {
                    if (!Cells[x, y].IsAlive || visited[x, y]) continue;

                    var group = new List<(int x, int y)>();
                    var queue = new Queue<(int x, int y)>();
                    queue.Enqueue((x, y));
                    visited[x, y] = true;

                    while (queue.Count > 0)
                    {
                        var current = queue.Dequeue();
                        group.Add(current);
                        for (int i = 0; i < dx.Length; i++)
                        {
                            int nx = current.x + dx[i];
                            int ny = current.y + dy[i];
                            if (nx < 0 || nx >= Columns || ny < 0 || ny >= Rows) continue;
                            if (visited[nx, ny] || !Cells[nx, ny].IsAlive) continue;
                            visited[nx, ny] = true;
                            queue.Enqueue((nx, ny));
                        }
                    }

                    result.Add(group);
                }
            }

            return result;
        }

        public Dictionary<string, int> ClassifyCombinations()
        {
            return GetCombinations()
                .Select(FigureClassifier.Classify)
                .GroupBy(name => name)
                .ToDictionary(group => group.Key, group => group.Count());
        }

        public void SaveState(string path)
        {
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
            using var writer = new StreamWriter(path);
            writer.WriteLine($"{Columns} {Rows} {CellSize}");
            for (int y = 0; y < Rows; y++)
            {
                for (int x = 0; x < Columns; x++)
                    writer.Write(Cells[x, y].IsAlive ? '1' : '0');
                writer.WriteLine();
            }
        }

        public static Board LoadState(string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("State file was not found.", path);
            var lines = File.ReadAllLines(path).Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
            if (lines.Length < 2) throw new InvalidDataException("State file is empty or malformed.");

            var header = lines[0].Split(new[] { ' ', '\t', ';', ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (header.Length < 2) throw new InvalidDataException("First line must contain columns and rows.");
            int columns = int.Parse(header[0]);
            int rows = int.Parse(header[1]);
            int cellSize = header.Length >= 3 ? int.Parse(header[2]) : 1;
            var board = new Board(columns * cellSize, rows * cellSize, cellSize, 0.0);

            for (int y = 0; y < rows && y + 1 < lines.Length; y++)
            {
                string row = new string(lines[y + 1].Where(ch => ch == '0' || ch == '1' || ch == '*' || ch == '.').ToArray());
                for (int x = 0; x < columns && x < row.Length; x++)
                    board.Cells[x, y].IsAlive = row[x] == '1' || row[x] == '*';
            }

            return board;
        }

        public void LoadColony(string path, int offsetX = 0, int offsetY = 0, bool clearBeforeLoad = true)
        {
            var colony = LoadState(path);
            if (clearBeforeLoad) Clear();
            for (int x = 0; x < colony.Columns; x++)
            {
                for (int y = 0; y < colony.Rows; y++)
                {
                    int targetX = x + offsetX;
                    int targetY = y + offsetY;
                    if (targetX >= 0 && targetX < Columns && targetY >= 0 && targetY < Rows)
                        Cells[targetX, targetY].IsAlive = colony.Cells[x, y].IsAlive;
                }
            }
        }

        public string Signature()
        {
            var chars = new char[Columns * Rows];
            int i = 0;
            for (int y = 0; y < Rows; y++)
                for (int x = 0; x < Columns; x++)
                    chars[i++] = Cells[x, y].IsAlive ? '1' : '0';
            return new string(chars);
        }

        private void ConnectNeighbors()
        {
            for (int x = 0; x < Columns; x++)
            {
                for (int y = 0; y < Rows; y++)
                {
                    int xL = x > 0 ? x - 1 : Columns - 1;
                    int xR = x < Columns - 1 ? x + 1 : 0;
                    int yT = y > 0 ? y - 1 : Rows - 1;
                    int yB = y < Rows - 1 ? y + 1 : 0;

                    Cells[x, y].neighbors.Add(Cells[xL, yT]);
                    Cells[x, y].neighbors.Add(Cells[x, yT]);
                    Cells[x, y].neighbors.Add(Cells[xR, yT]);
                    Cells[x, y].neighbors.Add(Cells[xL, y]);
                    Cells[x, y].neighbors.Add(Cells[xR, y]);
                    Cells[x, y].neighbors.Add(Cells[xL, yB]);
                    Cells[x, y].neighbors.Add(Cells[x, yB]);
                    Cells[x, y].neighbors.Add(Cells[xR, yB]);
                }
            }
        }
    }

    public static class FigureClassifier
    {
        private static readonly Dictionary<string, HashSet<string>> KnownPatterns = BuildPatterns();

        public static string Classify(List<(int x, int y)> cells)
        {
            if (cells == null || cells.Count == 0) return "Unknown";
            string normalized = Normalize(cells);
            foreach (var pair in KnownPatterns)
                if (pair.Value.Contains(normalized)) return pair.Key;
            return "Unknown";
        }

        private static Dictionary<string, HashSet<string>> BuildPatterns()
        {
            var patterns = new Dictionary<string, List<(int, int)>>
            {
                ["Block"] = new() { (0, 0), (1, 0), (0, 1), (1, 1) },
                ["Beehive"] = new() { (1, 0), (2, 0), (0, 1), (3, 1), (1, 2), (2, 2) },
                ["Loaf"] = new() { (1, 0), (2, 0), (0, 1), (3, 1), (1, 2), (3, 2), (2, 3) },
                ["Boat"] = new() { (0, 0), (1, 0), (0, 1), (2, 1), (1, 2) },
                ["Tub"] = new() { (1, 0), (0, 1), (2, 1), (1, 2) },
                ["Blinker"] = new() { (0, 0), (1, 0), (2, 0) },
            };

            return patterns.ToDictionary(pair => pair.Key, pair => GenerateVariants(pair.Value));
        }

        private static HashSet<string> GenerateVariants(List<(int x, int y)> pattern)
        {
            var variants = new HashSet<string>();
            foreach (bool swap in new[] { false, true })
            {
                foreach (int sx in new[] { -1, 1 })
                {
                    foreach (int sy in new[] { -1, 1 })
                    {
                        var transformed = pattern.Select(p => swap ? (x: p.y * sx, y: p.x * sy) : (x: p.x * sx, y: p.y * sy)).ToList();
                        variants.Add(Normalize(transformed));
                    }
                }
            }
            return variants;
        }

        private static string Normalize(IEnumerable<(int x, int y)> cells)
        {
            var list = cells.ToList();
            int minX = list.Min(p => p.x);
            int minY = list.Min(p => p.y);
            return string.Join(";", list.Select(p => (x: p.x - minX, y: p.y - minY)).OrderBy(p => p.y).ThenBy(p => p.x).Select(p => $"{p.x},{p.y}"));
        }
    }

    public static class StabilityAnalyzer
    {
        public static int GenerationsToStable(int width, int height, int cellSize, double density, int windowSize = 8, int maxGenerations = 500)
        {
            var board = new Board(width, height, cellSize, density);
            var aliveCounts = new Queue<int>();

            for (int generation = 0; generation <= maxGenerations; generation++)
            {
                aliveCounts.Enqueue(board.CountAlive());
                while (aliveCounts.Count > windowSize) aliveCounts.Dequeue();

                if (aliveCounts.Count == windowSize && aliveCounts.Distinct().Count() == 1)
                    return generation - windowSize + 1;

                board.Advance();
            }

            return maxGenerations;
        }

        public static Dictionary<double, double> RunExperiment(IEnumerable<double> densities, int trials = 10, int width = 50, int height = 30, int cellSize = 1, int windowSize = 8, int maxGenerations = 500)
        {
            var result = new Dictionary<double, double>();
            foreach (double density in densities)
            {
                double sum = 0;
                for (int i = 0; i < trials; i++)
                    sum += GenerationsToStable(width, height, cellSize, density, windowSize, maxGenerations);
                result[density] = sum / Math.Max(1, trials);
            }
            return result;
        }

        public static void SaveExperimentData(Dictionary<double, double> data, string path)
        {
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
            using var writer = new StreamWriter(path);
            writer.WriteLine("Density AvgGenerations");
            foreach (var pair in data.OrderBy(x => x.Key))
                writer.WriteLine(
                    $"{pair.Key.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)} " +
                    $"{pair.Value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}"
                );
        }
        public static void SavePlot(Dictionary<double, double> data, string path)
        {
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);

            const int width = 900;
            const int height = 600;
            const int left = 90;
            const int right = 40;
            const int top = 60;
            const int bottom = 80;

            byte[] pixels = Enumerable.Repeat((byte)255, width * height * 3).ToArray();

            void SetPixel(int x, int y, byte r, byte g, byte b)
            {
                if (x < 0 || x >= width || y < 0 || y >= height) return;
                int i = (y * width + x) * 3;
                pixels[i] = r;
                pixels[i + 1] = g;
                pixels[i + 2] = b;
            }

            void DrawLine(int x0, int y0, int x1, int y1, byte r, byte g, byte b)
            {
                int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
                int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
                int err = dx + dy;

                while (true)
                {
                    SetPixel(x0, y0, r, g, b);
                    if (x0 == x1 && y0 == y1) break;
                    int e2 = 2 * err;
                    if (e2 >= dy) { err += dy; x0 += sx; }
                    if (e2 <= dx) { err += dx; y0 += sy; }
                }
            }

            void DrawCircle(int cx, int cy, int radius, byte r, byte g, byte b)
            {
                for (int y = -radius; y <= radius; y++)
                {
                    for (int x = -radius; x <= radius; x++)
                    {
                        if (x * x + y * y <= radius * radius)
                            SetPixel(cx + x, cy + y, r, g, b);
                    }
                }
            }

            var ordered = data.OrderBy(x => x.Key).ToList();
            if (ordered.Count == 0) return;

            double minX = ordered.Min(x => x.Key);
            double maxX = ordered.Max(x => x.Key);
            double maxY = Math.Max(1, ordered.Max(x => x.Value));

            int PlotX(double x) =>
                left + (int)((x - minX) / (maxX - minX) * (width - left - right));

            int PlotY(double y) =>
                height - bottom - (int)(y / maxY * (height - top - bottom));

            // grid
            for (int i = 0; i <= 10; i++)
            {
                int x = left + i * (width - left - right) / 10;
                int y = top + i * (height - top - bottom) / 10;
                DrawLine(x, top, x, height - bottom, 210, 210, 210);
                DrawLine(left, y, width - right, y, 210, 210, 210);
            }

            // axes
            DrawLine(left, top, left, height - bottom, 0, 0, 0);
            DrawLine(left, height - bottom, width - right, height - bottom, 0, 0, 0);

            // line
            for (int i = 1; i < ordered.Count; i++)
            {
                int x0 = PlotX(ordered[i - 1].Key);
                int y0 = PlotY(ordered[i - 1].Value);
                int x1 = PlotX(ordered[i].Key);
                int y1 = PlotY(ordered[i].Value);
                DrawLine(x0, y0, x1, y1, 30, 120, 200);
            }

            // points
            foreach (var pair in ordered)
            {
                DrawCircle(PlotX(pair.Key), PlotY(pair.Value), 6, 30, 120, 200);
            }

            WritePng(path, width, height, pixels);
        }

        private static void WritePng(string path, int width, int height, byte[] rgb)
        {
            using var file = new FileStream(path, FileMode.Create, FileAccess.Write);

            file.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });

            void Chunk(string type, byte[] data)
            {
                byte[] typeBytes = Encoding.ASCII.GetBytes(type);
                file.Write(BitConverter.GetBytes(System.Net.IPAddress.HostToNetworkOrder(data.Length)));
                file.Write(typeBytes);
                file.Write(data);

                uint crc = Crc32(typeBytes.Concat(data).ToArray());
                file.Write(BitConverter.GetBytes(System.Net.IPAddress.HostToNetworkOrder((int)crc)));
            }

            byte[] ihdr = new byte[13];
            Array.Copy(BitConverter.GetBytes(System.Net.IPAddress.HostToNetworkOrder(width)), 0, ihdr, 0, 4);
            Array.Copy(BitConverter.GetBytes(System.Net.IPAddress.HostToNetworkOrder(height)), 0, ihdr, 4, 4);
            ihdr[8] = 8;
            ihdr[9] = 2;
            ihdr[10] = 0;
            ihdr[11] = 0;
            ihdr[12] = 0;
            Chunk("IHDR", ihdr);

            using var raw = new MemoryStream();
            for (int y = 0; y < height; y++)
            {
                raw.WriteByte(0);
                raw.Write(rgb, y * width * 3, width * 3);
            }

            using var compressed = new MemoryStream();
            using (var z = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
            {
                raw.Position = 0;
                raw.CopyTo(z);
            }

            Chunk("IDAT", compressed.ToArray());
            Chunk("IEND", Array.Empty<byte>());
        }

        private static uint Crc32(byte[] bytes)
        {
            uint crc = 0xffffffff;

            foreach (byte b in bytes)
            {
                crc ^= b;
                for (int i = 0; i < 8; i++)
                    crc = (crc & 1) != 0 ? 0xedb88320 ^ (crc >> 1) : crc >> 1;
            }

            return crc ^ 0xffffffff;
        }

    }

    public class Program
    {
        static Board board = new Board(50, 20, 1, 0.0);

        static void Reset(Settings settings)
        {
            board = new Board(settings.Width, settings.Height, settings.CellSize, settings.Density);
        }

        static void Render()
        {
            Console.SetCursorPosition(0, 0);
            for (int row = 0; row < board.Rows; row++)
            {
                for (int col = 0; col < board.Columns; col++)
                    Console.Write(board.Cells[col, row].IsAlive ? '*' : ' ');
                Console.WriteLine();
            }

            var groups = board.GetCombinations();
            Console.WriteLine($"Alive: {board.CountAlive()}, combinations: {groups.Count}");
            var classified = board.ClassifyCombinations();
            foreach (var pair in classified.OrderBy(x => x.Key))
                Console.Write($"{pair.Key}: {pair.Value}  ");
            Console.WriteLine();
            Console.WriteLine("Keys: S-save, L-load, R-random reset, Q-quit");
        }

        static void RunExperimentCommand()
        {
            double[] densities = { 0.05, 0.15, 0.25, 0.35, 0.45, 0.55, 0.65, 0.75, 0.85 };

            var data = StabilityAnalyzer.RunExperiment(
                densities,
                trials: 10,
                width: 50,
                height: 30
            );

            StabilityAnalyzer.SaveExperimentData(data, Path.Combine("Data", "data.txt"));
            StabilityAnalyzer.SavePlot(data, Path.Combine("Data", "plot.png"));

            Console.WriteLine("Experiment data and plot saved to Data/data.txt and Data/plot.png.");
        }

        public static void Main(string[] args)
        {
            var settings = Settings.Load("settings.json");

            if (args.Contains("--experiment"))
            {
                RunExperimentCommand();
                return;
            }

            Reset(settings);
            string? colonyArg = args.FirstOrDefault(a => a.StartsWith("--colony=", StringComparison.OrdinalIgnoreCase));
            if (colonyArg != null)
                board.LoadColony(colonyArg.Split('=', 2)[1], 2, 2);

            Console.CursorVisible = false;
            for (int generation = 0; generation < settings.Generations; generation++)
            {
                Render();
                if (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(true).Key;
                    if (key == ConsoleKey.Q) break;
                    if (key == ConsoleKey.S) board.SaveState("saved_state.txt");
                    if (key == ConsoleKey.L && File.Exists("saved_state.txt")) board = Board.LoadState("saved_state.txt");
                    if (key == ConsoleKey.R) Reset(settings);
                }
                board.Advance();
                Thread.Sleep(settings.DelayMs);
            }
            Console.CursorVisible = true;
        }
    }
}
