using System.Collections.Generic;
using UnityEngine;

public class OptimizationReport
{
    public enum Severity
    {
        High,
        Medium,
        Low
    }

    public struct Entry
    {
        public string warning;
        public string suggestion;
        public Severity severity;

        public Entry(string warning, string suggestion, Severity severity)
        {
            this.warning = warning;
            this.suggestion = suggestion;
            this.severity = severity;
        }
    }

    public List<Entry> entries = new List<Entry>();
    public List<Object> problemObjects = new List<Object>();

    public void Add(string warn, string suggest, Severity severity = Severity.Medium)
    {
        entries.Add(new Entry(warn, suggest, severity));
    }

    public void AddObjects(IEnumerable<Object> objs)
    {
        problemObjects.AddRange(objs);
    }
}
