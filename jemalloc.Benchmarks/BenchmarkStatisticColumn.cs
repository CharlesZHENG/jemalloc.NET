﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Filters;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Columns;
namespace jemalloc.Benchmarks
{
    public class BenchmarkStatisticColumn : IColumn
    {
        #region Constructors
        public BenchmarkStatisticColumn(string columnName, string legend)
        {
            ColumnName = columnName;
            Legend = legend;
        }
        #endregion

        #region Implemented properties
        //
        // Summary:
        //     An unique identificator of the column. If there are several columns with the
        //     same Id, only one of them will be shown in the summary.
        public string Id => ColumnName;
        //
        // Summary:
        //     Defines order of column in the same category.
        public int PriorityInCategory { get; } = 99;

        // Summary:
        //     Defines how to format column's value
        public UnitType UnitType { get; } = UnitType.Size;
        //
        // Summary:
        //     Column description.
        public string Legend { get; protected set; }

        public bool AlwaysShow => true;

        public bool IsNumeric => true;

        public ColumnCategory Category => ColumnCategory.Statistics;

        #endregion

        #region Implemented methods
        public bool IsDefault(Summary summary, Benchmark benchmark) => false;

        public string GetValue(Summary summary, Benchmark benchmark)
        {
            if (JemUtil.BenchmarkStatistics.ContainsKey($"{benchmark.Target.Method.Name}_{ColumnName}_{benchmark.Parameters[0].Value.GetHashCode()}"))
            {       
                return JemUtil.BenchmarkStatistics[$"{benchmark.Target.Method.Name}_{ColumnName}_{benchmark.Parameters[0].Value.GetHashCode()}"];
            }
            else if (JemUtil.BenchmarkStatistics.ContainsKey($"{benchmark.Target.Method.Name}_{ColumnName}"))
            {
                return JemUtil.BenchmarkStatistics[$"{benchmark.Target.Method.Name}_{ColumnName}"];
            }
            else return string.Empty;
            /*
            string[] catgegories = new string[2] { "Managed", "Unmanaged" };
            foreach (string c in catgegories)
            {
                if (benchmark.Target.Categories.Contains(c))
                {
                    if (JemUtil.BenchmarkStatistics.ContainsKey($"{c}_{ColumnName}"))
                    {
                        return JemUtil.BenchmarkStatistics[$"{c}_{ColumnName}"];
                    }
                }
            }*/

        }

        public bool IsAvailable(Summary summary)
        {
            foreach(Benchmark benchmark in summary.Benchmarks)
            {
                if (JemUtil.BenchmarkStatistics.ContainsKey($"{benchmark.Target.Method.Name}_{ColumnName}"))
                {
                    return true;
                }
            }
            return false;
        }
       
        public string GetValue(Summary summary, Benchmark benchmark, ISummaryStyle style) => GetValue(summary, benchmark);

        public string ColumnName { get; }
        #endregion

        #region Overriden methods
        public override string ToString() => ColumnName;
        #endregion

        #region Available columns
        public static readonly IColumn PrivateMemory = new BenchmarkStatisticColumn("PrivateMemory", "Total memory allocated for process(native and managed, 1KB = 1024B)");
        public static readonly IColumn WorkingSet = new BenchmarkStatisticColumn("WorkingSet", "Memory that is currently being used by process(native and managed, 1KB = 1024B)");
        public static readonly IColumn JemAllocated = new BenchmarkStatisticColumn("JemAllocated", "Total memory allocated by jemalloc(native only, inclusive, 1KB = 1024B)");
        public static readonly IColumn JemResident = new BenchmarkStatisticColumn("JemResident", "Resident memory allocated by jemalloc(native only, inclusive, 1KB = 1024B)");
        public static readonly IColumn ThreadCycles = new BenchmarkStatisticColumn("ThreadCycles", "RDTSC time elapsed.");
        public static readonly IColumn ISPCResult = new BenchmarkStatisticColumn("ISPCResult", "Result obtained with Intel ISPC compiler on a AVX2 CPU.");
        public static readonly IColumn ISPCResult2 = new BenchmarkStatisticColumn("ISPCResult2", "Result obtained with Intel ISPC compiler on a SSE4.2 CPU.");
        #endregion
    }
}
