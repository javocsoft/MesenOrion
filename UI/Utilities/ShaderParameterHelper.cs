using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Mesen.Utilities
{
	//Parses libretro-style "#pragma parameter" declarations directly from a shader
	//file (.glslp preset or single .glsl), so the UI can show a shader's adjustable
	//parameters without depending on the render thread having loaded the shader.
	public static class ShaderParameterHelper
	{
		public class ShaderParam
		{
			public string Name { get; set; } = "";
			public string Desc { get; set; } = "";
			public double Min { get; set; } = 0;
			public double Max { get; set; } = 1;
			public double Step { get; set; } = 0.01;
			public double Default { get; set; } = 0;
		}

		public static List<ShaderParam> Parse(string? path)
		{
			List<ShaderParam> result = new List<ShaderParam>();
			if(string.IsNullOrEmpty(path) || !File.Exists(path)) {
				return result;
			}

			List<string> shaderFiles = new List<string>();
			Dictionary<string, double> presetOverrides = new Dictionary<string, double>();

			string ext = Path.GetExtension(path).ToLowerInvariant();
			if(ext == ".glslp") {
				string dir = Path.GetDirectoryName(path) ?? "";
				List<string> paramNames = new List<string>();
				Dictionary<string, string> kv = new Dictionary<string, string>();
				try {
					foreach(string line in File.ReadAllLines(path)) {
						string t = line.Trim();
						if(t.Length == 0 || t[0] == '#') {
							continue;
						}
						int eq = t.IndexOf('=');
						if(eq < 0) {
							continue;
						}
						string key = t.Substring(0, eq).Trim();
						string val = t.Substring(eq + 1).Trim().Trim('"');
						kv[key] = val;
						if(key.StartsWith("shader") && key.Length > 6 && char.IsDigit(key[6])) {
							shaderFiles.Add(Path.Combine(dir, val));
						}
					}
				} catch {
					return result;
				}

				if(kv.TryGetValue("parameters", out string? plist)) {
					paramNames.AddRange(plist.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()));
				}
				foreach(string name in paramNames) {
					if(kv.TryGetValue(name, out string? v) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double dv)) {
						presetOverrides[name] = dv;
					}
				}
			} else {
				shaderFiles.Add(path);
			}

			foreach(string sf in shaderFiles) {
				if(!File.Exists(sf)) {
					continue;
				}
				string[] lines;
				try {
					lines = File.ReadAllLines(sf);
				} catch {
					continue;
				}
				foreach(string line in lines) {
					string t = line.Trim();
					const string tag = "#pragma parameter";
					if(!t.StartsWith(tag)) {
						continue;
					}
					string rest = t.Substring(tag.Length).Trim();
					int sp = rest.IndexOfAny(new[] { ' ', '\t' });
					if(sp < 0) {
						continue;
					}
					string name = rest.Substring(0, sp);
					if(result.Any(x => x.Name == name)) {
						continue;
					}

					string desc = name;
					double init = 0, min = 0, max = 1, step = 0.01;
					int q1 = rest.IndexOf('"');
					int q2 = q1 >= 0 ? rest.IndexOf('"', q1 + 1) : -1;
					if(q1 >= 0 && q2 > q1) {
						desc = rest.Substring(q1 + 1, q2 - q1 - 1);
						string[] nums = rest.Substring(q2 + 1).Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
						if(nums.Length > 0) { double.TryParse(nums[0], NumberStyles.Float, CultureInfo.InvariantCulture, out init); }
						if(nums.Length > 1) { double.TryParse(nums[1], NumberStyles.Float, CultureInfo.InvariantCulture, out min); }
						if(nums.Length > 2) { double.TryParse(nums[2], NumberStyles.Float, CultureInfo.InvariantCulture, out max); }
						if(nums.Length > 3) { double.TryParse(nums[3], NumberStyles.Float, CultureInfo.InvariantCulture, out step); }
					}

					if(presetOverrides.TryGetValue(name, out double ov)) {
						init = ov;
					}

					result.Add(new ShaderParam {
						Name = name,
						Desc = string.IsNullOrEmpty(desc) ? name : desc,
						Min = min,
						Max = max,
						Step = step <= 0 ? 0.01 : step,
						Default = init
					});
				}
			}

			return result;
		}
	}
}
