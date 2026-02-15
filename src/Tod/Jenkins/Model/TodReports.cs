using System.Xml.Linq;

namespace Tod.Jenkins;

internal static class TodReports
{
    public static XElement GetHead()
    {
        return new XElement("head",
            new XElement("meta",
                new XAttribute("charset", "UTF-8")),
            new XElement("style", @"
body {
  font-family: ""Segoe UI"", Roboto, sans-serif;
  background: #f9f9fb;
  color: #333;
  margin: 20px;
}

table {
  border-collapse: collapse;
  width: 100%;
  margin-bottom: 20px;
  background: #fff;
  box-shadow: 0 2px 4px rgba(0,0,0,0.05);
  table-layout: auto;
}

th, td {
  padding: 8px 12px;
  text-align: left;
  border-bottom: 1px solid #eee;
}

th {
  background: #f0f2f5;
  font-weight: 600;
}

tr:hover {
  background: #f9f9f9;
}

a {
  color: #0078d4;
  text-decoration: none;
}

a:hover {
  text-decoration: underline;
}

table.tests {
  border-collapse: collapse;
  width: 100%;
  font-family: ""Segoe UI"", Roboto, sans-serif;
  font-size: 13px;
}

table.tests th, table.tests td {
  padding: 6px 10px;
  text-align: left;
  border-bottom: 1px solid #eee;
}

/* alternative colors
table.tests th {
  background: #f0f2f5;
  font-weight: 600;
}

table.tests tr:nth-child(even) {
  background-color: #f9f9fb;
}

table.tests tr:nth-child(odd) {
  background-color: #ffffff;
}
*/

table.tests th {
  background: #e8f0fe;
  font-weight: 600;
}

table.tests tr:nth-child(even) {
  background-color: #f0f8ff;
}

table.tests tr:nth-child(odd) {
  background-color: #ffffff;
}

.test-info {  
  font-size: 0.9em;
  color: #555;
  width: 1%;
  white-space: nowrap;
}

pre {
  white-space: normal;
}

.test-name {
  font-family: monospace;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  max-width: 600px;
  font-size: 11px;
}

.label {
  display: inline-block;
  padding: 2px 6px;
  margin: 2px 4px 2px 0;
  font-size: 0.85em;
  font-weight: 500;
  border-radius: 8px;
  color: #fff;
}

.label.new {
  background-color: #e74c3c;
}

.label.unstable {
  background-color: #f39c12;
}")
        );
    }

    public static string Shorten(string details)
    {
        if (details.Length <= 2100)
        {
            return details;
        }
        return string.Concat(details.AsSpan(0, 2000), "...");
    }
}
