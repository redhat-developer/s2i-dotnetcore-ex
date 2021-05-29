using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata.Ecma335;
using System.Text.RegularExpressions;
using System.Threading;
using Binance.Net;
using Discord;

namespace MarketPriceGap
{
    // ReSharper disable once ClassNeverInstantiated.Global
    class Program
    {
        static BinanceClient Client = new BinanceClient();

        static void Main(string[] args)
        {
            string path = "/src";
            string newPath = path;
            var webhook = Regex.Split(File.ReadAllText(newPath + "/webhook.txt"), "/");
            var webhookId = ulong.Parse(webhook[0]);
            var token = webhook[1];
            var discord = new Discord(webhookId, token);

            int interval = 5000;
            string[] pairs = { "BTCUSDT" };
            // ReSharper disable once IdentifierTypo
            Dictionary<string, int> gaplist = new Dictionary<string, int>();

            while (true)
            {
                foreach (var pair in pairs)
                {
                    var market = GetMarketPrice(pair);
                    if (market == null) {
                        continue;
                    }

                    var price = GetPrice(pair);
                    var gapPercent = GetPrcentAbs(pair, market.Value, price);
                    if (gapPercent >= 1)
                    {
                        if (!gaplist.ContainsKey(pair))
                        {
                            gaplist.Add(pair, 0);
                        }
                        else
                        {
                            gaplist[pair]++;
                            if (gaplist[pair] > 1)
                            {

                                Console.WriteLine($"Pair: {pair} \r\nPriceGap: {gapPercent:##.##}");
                                discord.Send(pair, $"Market price: {price}\r\nPrice: {price}\r\nGap: {gapPercent:##.##}%", Color.Gold);
                            }
                        }
                    }
                    else
                    {
                        if (gaplist.ContainsKey(pair))
                        {
                            gaplist.Remove(pair);
                        }
                    }
                }

                GC.Collect();
                Thread.Sleep(interval);
            }
        }

        static decimal? GetMarketPrice(string pair)
        {
            try {
                return Client.FuturesUsdt.Market.GetMarkPrices(pair).Data.Last().MarkPrice;
            } catch {
                return null;
            }
        }

        static decimal GetPrice(string pair)
        {
            try {
                return Client.FuturesUsdt.Market.GetPrice(pair).Data.Price == 0 ? 1 : Client.FuturesUsdt.Market.GetPrice(pair).Data.Price;
            }catch {
                return 1;
            }
        }

        static decimal GetPrcentAbs(string pair, decimal value1, decimal value2)
        {
            var percentage = value1 / value2 * 100 - 100;
            return percentage < 0 ? percentage * -1 : percentage;
        }
    }
}
