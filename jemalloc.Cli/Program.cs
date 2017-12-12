﻿using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Reflection;

using Serilog;
using CommandLine;
using CommandLine.Text;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Filters;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Reports;

using jemalloc.Benchmarks;

namespace jemalloc.Cli
{
    class Program
    {
        public enum ExitResult
        {
            SUCCESS = 0,
            UNHANDLED_EXCEPTION = 1,
            INVALID_OPTIONS = 2
        }

        public enum Category
        {
            MALLOC,
            NARRAY,
            HUGEARRAY,
            BUFFER
        }

        public enum Operation
        {
            CREATE,
            FILL
        }

        static Version Version = Assembly.GetExecutingAssembly().GetName().Version;
        static LoggerConfiguration LConfig;
        static ILogger L;
        static Dictionary<string, object> BenchmarkOptions = new Dictionary<string, object>();
        static Summary BenchmarkSummary;

        static void Main(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            LConfig = new LoggerConfiguration()
                .Enrich.FromLogContext()
                .Enrich.WithThreadId()
                .Enrich.WithProcessId()
                .MinimumLevel.Debug()
                .WriteTo.Console(outputTemplate: "{Timestamp:HH:mm:ss}<{ThreadId:d2}> [{Level:u3}] {Message}{NewLine}{Exception}");
            L = Log.Logger = LConfig.CreateLogger();
            Type[] BenchmarkOptionTypes = Assembly.GetExecutingAssembly().GetTypes().Where(t => t.IsSubclassOf(typeof(Options))).ToArray();
            ParserResult<object> result = new Parser().ParseArguments<Options, MallocBenchmarkOptions, NativeArrayBenchmarkOptions, HugeNativeArrayBenchmarkOptions>(args);
            result.WithNotParsed((IEnumerable<Error> errors) =>
            {
                HelpText help = GetAutoBuiltHelpText(result);
                help.MaximumDisplayWidth = Console.WindowWidth;
                help.Copyright = string.Empty;
                help.Heading = new HeadingInfo("jemalloc.NET", Version.ToString(3));
                help.AddPreOptionsLine(string.Empty);
                if (errors.Any(e => e.Tag == ErrorType.VersionRequestedError))
                {
                    Log.Information(help);
                    Exit(ExitResult.SUCCESS);
                }
                else if (errors.Any(e => e.Tag == ErrorType.HelpVerbRequestedError))
                {
                    HelpVerbRequestedError error = (HelpVerbRequestedError)errors.First(e => e.Tag == ErrorType.HelpVerbRequestedError);
                    if (error.Type != null)
                    {
                        help.AddVerbs(error.Type);
                    }
                    Log.Information(help);
                    Exit(ExitResult.SUCCESS);
                }
                else if (errors.Any(e => e.Tag == ErrorType.HelpRequestedError))
                {
                    help.AddVerbs(BenchmarkOptionTypes);
                    L.Information(help);
                    Exit(ExitResult.SUCCESS);
                }
                else if (errors.Any(e => e.Tag == ErrorType.NoVerbSelectedError))
                {
                    help.AddVerbs(BenchmarkOptionTypes);
                    help.AddPreOptionsLine("No category selected. Select a category from the options below:");
                    L.Information(help);
                    Exit(ExitResult.INVALID_OPTIONS);
                }
                else if (errors.Any(e => e.Tag == ErrorType.MissingRequiredOptionError))
                {
                    MissingRequiredOptionError error = (MissingRequiredOptionError)errors.First(e => e is MissingRequiredOptionError);
                    help.AddOptions(result);
                    help.AddPreOptionsLine($"A required option or value is missing. The options and values for this benchmark category are: ");
                    L.Information(help);
                    Exit(ExitResult.INVALID_OPTIONS);
                }
                else if (errors.Any(e => e.Tag == ErrorType.MissingValueOptionError))
                {
                    MissingValueOptionError error = (MissingValueOptionError)errors.First(e => e.Tag == ErrorType.MissingValueOptionError);
                    help.AddOptions(result);
                    help.AddPreOptionsLine($"A required option or value is missing. The options and values for this benchmark category are: ");
                    L.Information(help);
                    Exit(ExitResult.INVALID_OPTIONS);
                }
                else if (errors.Any(e => e.Tag == ErrorType.UnknownOptionError))
                {
                    UnknownOptionError error = (UnknownOptionError)errors.First(e => e.Tag == ErrorType.UnknownOptionError);
                    help.AddOptions(result);
                    help.AddPreOptionsLine($"Unknown option: {error.Token}.");
                    L.Information(help);
                    Exit(ExitResult.INVALID_OPTIONS);
                }
                else
                {
                    help.AddPreOptionsLine($"An error occurred parsing the program options: {string.Join(' ', errors.Select(e => e.Tag.ToString()).ToArray())}");
                    help.AddVerbs(BenchmarkOptionTypes);
                    L.Information(help);
                    Exit(ExitResult.INVALID_OPTIONS);
                }
            })
            .WithParsed((Options o) =>
            {
              
                foreach (PropertyInfo prop in o.GetType().GetProperties())
                {
                    BenchmarkOptions.Add(prop.Name, prop.GetValue(o));
                }
            })
            .WithParsed<MallocBenchmarkOptions>(o =>
            {
                BenchmarkOptions.Add("Category", Category.MALLOC);
                if (o.Create)
                {
                    BenchmarkOptions.Add("Operation", Operation.CREATE);
                }
                else if (o.Fill)
                {
                    BenchmarkOptions.Add("Operation", Operation.FILL);
                }

                if (!BenchmarkOptions.ContainsKey("Operation"))
                {
                    Log.Error("You must select an operation to benchmark with --fill.");
                    Exit(ExitResult.SUCCESS);
                }
                else
                {
                    Benchmark(o);
                }
            })

            .WithParsed<NativeArrayBenchmarkOptions>(o =>
            {
                BenchmarkOptions.Add("Category", Category.NARRAY);
                if (o.Create)
                {
                    BenchmarkOptions.Add("Operation", Operation.CREATE);
                }
                else if (o.Fill)
                {
                    BenchmarkOptions.Add("Operation", Operation.FILL);
                }
                if (!BenchmarkOptions.ContainsKey("Operation"))
                {
                    Log.Error("You must select an operation to benchmark with --create or --fill.");
                    Exit(ExitResult.SUCCESS);
                }
                else
                {
                    Benchmark(o);
                }
                
            })

            .WithParsed<HugeNativeArrayBenchmarkOptions>(o =>
            {
                BenchmarkOptions.Add("Category", Category.HUGEARRAY);
                if (o.Create)
                {
                    BenchmarkOptions.Add("Operation", Operation.CREATE);
                }
                else if (o.Fill)
                {
                    BenchmarkOptions.Add("Operation", Operation.FILL);
                }

                if (!BenchmarkOptions.ContainsKey("Operation"))
                {
                    Log.Error("You must select an operation to benchmark with --create or --fill.");
                    Exit(ExitResult.SUCCESS);
                }
                else
                {
                    Benchmark(o);
                }
            }); 
        }

        private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Log.Error((Exception) e.ExceptionObject, "An unhandled exception occurred. The program will now shutdown.");
            Exit(ExitResult.UNHANDLED_EXCEPTION);
        }

        static void Benchmark(Options o)
        {
            Contract.Requires(BenchmarkOptions.ContainsKey("Operation"));
            Contract.Requires(BenchmarkOptions.ContainsKey("Category"));
            if (o.Int16 && o.Unsigned)
            {
                Benchmark<UInt16>();
            }
            else if (o.Int16)
            {
                Benchmark<Int16>();
            }
            else if (o.Int32 && o.Unsigned)
            {
                Benchmark<UInt32>();
            }
            else if (o.Int32)
            {
                Benchmark<Int32>();
            }
            else if (o.Int64 && o.Unsigned)
            {
                Benchmark<UInt64>();
            }
            else if (o.Int64)
            {
                Benchmark<Int64>();
            }
            else
            {
                L.Error("You must select a data type to benchmark with -i, -s, or -l.");
                Exit(ExitResult.INVALID_OPTIONS);
            }
        }
        static void Benchmark<T>() where T : struct
        {
            Contract.Requires(BenchmarkOptions.ContainsKey("Category"));
            Contract.Requires(BenchmarkOptions.ContainsKey("Operation"));
            IConfig config = ManualConfig
                         .Create(DefaultConfig.Instance);
            try
            {
                switch ((Category)BenchmarkOptions["Category"])
                {
                    case Category.MALLOC:
                        MallocVsArrayBenchmark<T>.BenchmarkParameters = (IEnumerable<int>)BenchmarkOptions["Sizes"];
                        switch ((Operation)BenchmarkOptions["Operation"])
                        {
                            case Operation.CREATE:
                                config = config.With(new NameFilter(name => name.Contains("Create")));
                                L.Information("Starting {num} create benchmarks for data type {t} with array sizes: {s}", JemBenchmark<T, int>.GetBenchmarkMethodCount<MallocVsArrayBenchmark<T>>(),
                                    typeof(T).Name, MallocVsArrayBenchmark<T>.BenchmarkParameters);
                                L.Information("Please allow some time for the pilot and warmup phases of the benchmark.");
                                break;

                            case Operation.FILL:
                                config = config.With(new NameFilter(name => name.Contains("Fill")));
                                L.Information("Starting {num} fill benchmarks for data type {t} with array sizes: {s}", JemBenchmark<T, int>.GetBenchmarkMethodCount<MallocVsArrayBenchmark<T>>(),
                                    typeof(T).Name, MallocVsArrayBenchmark<T>.BenchmarkParameters);
                                L.Information("Please allow some time for the pilot and warmup phases of the benchmark.");
                        
                                break;
                            default:
                                throw new InvalidOperationException($"Unknown operation: {(Operation)BenchmarkOptions["Operation"]} for category {(Category)BenchmarkOptions["Category"]}.");
                        }
                        BenchmarkSummary = BenchmarkRunner.Run<MallocVsArrayBenchmark<T>>(config);
                        break;

                    case Category.NARRAY:
                        NativeVsManagedArrayBenchmark<int>.BenchmarkParameters = (IEnumerable<int>)BenchmarkOptions["Sizes"];
                        switch ((Operation)BenchmarkOptions["Operation"])
                        {
                            case Operation.CREATE:
                                L.Information("Starting {num} create array benchmarks with array sizes: {s}",
                                    NativeVsManagedArrayBenchmark<int>.BenchmarkParameters);
                                L.Information("Please allow some time for the pilot and warmup phases of the benchmark.");
                                config = config
                                    .With(new NameFilter(name => name.Contains("Create")));
                                break;

                            case Operation.FILL:
                                L.Information("Starting {num} array fill benchmarks for data type {t} with array sizes: {s}",
                                    JemBenchmark<T, int>.GetBenchmarkMethodCount<NativeVsManagedArrayBenchmark<T>>(),
                                    typeof(T).Name, NativeVsManagedArrayBenchmark<T>.BenchmarkParameters);
                                L.Information("Please allow some time for the pilot and warmup phases of the benchmark.");
                                config = config
                                    .With(new NameFilter(name => name.Contains("Fill")));
                                break;

                            default:
                                throw new InvalidOperationException($"Unknown operation: {(Operation)BenchmarkOptions["Operation"]} for category {(Category)BenchmarkOptions["Category"]}.");
                        }
                        BenchmarkSummary = BenchmarkRunner.Run<NativeVsManagedArrayBenchmark<int>>(config);
                        break;

                    case Category.HUGEARRAY:
                        HugeNativeVsManagedArrayBenchmark<T>.BenchmarkParameters = (IEnumerable<ulong>)BenchmarkOptions["Sizes"];
                        switch ((Operation)BenchmarkOptions["Operation"])
                        {
                            case Operation.CREATE:
                                config = config.With(new NameFilter(name => name.Contains("Create")));
                                L.Information("Starting {num} huge array create benchmarks for data type {t} with array sizes: {s}",
                                    JemBenchmark<T, ulong>.GetBenchmarkMethodCount<HugeNativeVsManagedArrayBenchmark<T>>(),
                                    typeof(T).Name, HugeNativeVsManagedArrayBenchmark<T>.BenchmarkParameters);
                                L.Information("Please allow some time for the pilot and warmup phases of the benchmark.");
                                L.Warning("This benchmark can take a long time (up to 15 mins).");
                                break;

                            case Operation.FILL:
                                config = config.With(new NameFilter(name => name.Contains("Fill")));
                                L.Information("Starting {num} huge array fill benchmarks for data type {t} with array sizes: {s}",
                                    JemBenchmark<T, ulong>.GetBenchmarkMethodCount<HugeNativeVsManagedArrayBenchmark<T>>(),
                                    typeof(T).Name, HugeNativeVsManagedArrayBenchmark<T>.BenchmarkParameters);
                                L.Information("Please allow some time for the pilot and warmup phases of the benchmark.");
                                break;

                            default:
                                throw new InvalidOperationException($"Unknown operation: {(Operation)BenchmarkOptions["Operation"]} for category {(Category)BenchmarkOptions["Category"]}.");
                        }
                        BenchmarkSummary = BenchmarkRunner.Run<HugeNativeVsManagedArrayBenchmark<T>>(config);
                        break;
                    default:
                        throw new InvalidOperationException($"Unknown category: {(Category)BenchmarkOptions["Category"]}.");
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Exception thrown during benchmark.");
                Exit(ExitResult.UNHANDLED_EXCEPTION);
            }
        }

        static void Exit(ExitResult result)
        {
            Log.CloseAndFlush();
            
            Environment.Exit((int)result);
        }

        static int ExitWithCode(ExitResult result)
        {
            Log.CloseAndFlush();
            return (int)result;
        }

        static HelpText GetAutoBuiltHelpText(ParserResult<object> result)
        {
            return HelpText.AutoBuild(result, h =>
            {
                h.AddOptions(result);
                return h;
            },
            e =>
            {
                return e;
            });
        }

       
    }
}
