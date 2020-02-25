/*
 Copyright (C) 2019 TemporalAgent7 <https://github.com/TemporalAgent7>

 This file is part of the DataCore Bot open source project.

 This program is free software; you can redistribute it and/or modify
 it under the terms of the GNU General Public License as published by
 the Free Software Foundation; either version 3 of the License, or
 (at your option) any later version.

 This library is distributed in the hope that it will be useful,
 but WITHOUT ANY WARRANTY; without even the implied warranty of
 MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 GNU Lesser General Public License for more details.

 You should have received a copy of the GNU Lesser General Public License
 along with DataCore Bot; if not, see <http://www.gnu.org/licenses/>.
*/
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Runtime.Serialization.Formatters.Binary;
using Newtonsoft.Json;

namespace DataCore.Library
{
    public class Trainer
    {
        private static void SaveToDisk(IEnumerable<ImageIndex> data, string outPath)
        {
            BinaryFormatter formatter = new BinaryFormatter();
            foreach (var item in data)
            {
                using (Stream ms = File.OpenWrite(Path.Combine(outPath, "data", "traindata", $"{item.Symbol}.bin")))
                {
                    formatter.Serialize(ms, item);
                }
            }
        }

        public static IEnumerable<ImageIndex> LoadFromDisk(string outPath)
        {
            List<ImageIndex> result = new List<ImageIndex>();

            BinaryFormatter formatter = new BinaryFormatter();
            foreach (var file in Directory.EnumerateFiles(Path.Combine(outPath, "data", "traindata"), "*.bin"))
            {
                using (FileStream fs = File.Open(file, FileMode.Open))
                {
                    result.Add((ImageIndex)formatter.Deserialize(fs));
                }
            }

            return result;
        }

        private static byte[] ReadAllBytes(BinaryReader reader)
        {
            const int bufferSize = 4096;
            using (var ms = new MemoryStream())
            {
                byte[] buffer = new byte[bufferSize];
                int count;
                while ((count = reader.Read(buffer, 0, buffer.Length)) != 0)
                    ms.Write(buffer, 0, count);
                return ms.ToArray();
            }
        }

        public static void ParseDataset(string datacorepath, string outPath, bool noimages, Action<string> progress)
        {
            var crewPath = Path.Combine(datacorepath, "static", "structured", "crew.json");
            var assetPath = Path.Combine(datacorepath, "static", "media", "assets");
            Crew[] allcrew = JsonConvert.DeserializeObject<Crew[]>(File.ReadAllText(crewPath));
            SURFDescriptor descriptor = new SURFDescriptor();
            List<ImageIndex> imgIndexes = new List<ImageIndex>();

            bool useWeb = false;

            foreach (Crew crew in allcrew)
            {
                if (crew.max_rarity < 4)
                {
                    continue;
                }

                progress($"Parsing {crew.name}...");

                Mat image;

                // parse from web instead (since assets are no longer committed to GitHub)
                if (useWeb)
                {
                    using (var client = new WebClient())
                    {
                        using (BinaryReader reader = new BinaryReader(client.OpenRead($"https://assets.datacore.app/{crew.imageUrlFullBody}")))
                        {
                            image = Cv2.ImDecode(ReadAllBytes(reader), ImreadModes.Color);
                        }
                    }
                }
                else
                {
                    string fileName = Path.Combine(assetPath, crew.imageUrlFullBody);
                    image = Cv2.ImRead(fileName, ImreadModes.Unchanged);
                }

                ImageIndex imgIndex = fromImage(descriptor, noimages, outPath, crew, image);
                if (imgIndex != null)
                {
                    imgIndexes.Add(imgIndex);
                }
            }

            SaveToDisk(imgIndexes, outPath);
        }

        private static ImageIndex fromImage(SURFDescriptor descriptor, bool noimages, string outPath, Crew crew, Mat image)
        {
            image = image.SubMat(0, image.Rows * 7 / 10, 0, image.Cols);

            if (!noimages)
            {
                Cv2.ImWrite(Path.Combine(outPath, "data", "traindata", $"feat_{crew.symbol}.png"), image);
            }

            KeyPoint[] keypoints;
            var features = descriptor.Describe(image, out keypoints);

            if ((features.Rows > 0) && (features.Cols > 0))
            {
                float[] featuresBytes = new float[features.Rows * features.Cols];
                features.GetArray(out featuresBytes);

                return new ImageIndex()
                {
                    Features = featuresBytes,
                    MType = features.Type(),
                    Cols = features.Cols,
                    Rows = features.Rows,
                    Symbol = crew.symbol,
                    Rarity = crew.max_rarity
                };
            }

            return null;
        }

        public static void ParseSingleImage(string imagePath, string symbol, string outPath)
        {
            SURFDescriptor descriptor = new SURFDescriptor();
            List<ImageIndex> imgIndexes = new List<ImageIndex>();

            Mat image = Cv2.ImRead(imagePath, ImreadModes.Unchanged);

            KeyPoint[] keypoints;
            var features = descriptor.Describe(image, out keypoints);

            if ((features.Rows > 0) && (features.Cols > 0))
            {
                float[] featuresBytes = new float[features.Rows * features.Cols];
                features.GetArray(out featuresBytes);

                var imgIdx = new List<ImageIndex>();
                imgIdx.Add(new ImageIndex()
                {
                    Features = featuresBytes,
                    MType = features.Type(),
                    Cols = features.Cols,
                    Rows = features.Rows,
                    Symbol = symbol,
                    Rarity = 0
                });

                SaveToDisk(imgIdx, outPath);
            }
        }
    }
}
