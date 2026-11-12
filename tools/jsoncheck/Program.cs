// Fails when a type that goes into the Mars save data (System.Text.Json) has a Unity member: Vector3 and friends
// have properties that return themselves (normalized), and the serializer then gives up (2026-11-12 report).
using System; using System.Reflection; using System.Text.Json; using System.Linq;
class P { static int Main() {
  var asm = Assembly.LoadFrom(System.IO.Path.Combine(AppContext.BaseDirectory, "MarsMod.dll"));
  var opts = new JsonSerializerOptions { IncludeFields = true };
  int bad = 0;
  foreach (var name in new[] { "MarsMod.Buildings+State", "MarsMod.Buildings+Stock", "MarsMod.Goals+State", "MarsMod.Belts+State", "MarsMod.LogicGates+State", "MarsMod.Market+State" }) {
    var t = asm.GetType(name);
    if (t == null) { Console.WriteLine("missing " + name); bad++; continue; }
    // Walk the members the serializer would touch and flag any Unity type.
    void Walk(Type ty, string path, int depth) {
      if (depth > 6 || ty.IsPrimitive || ty == typeof(string) || ty.IsEnum) return;
      if (ty.Namespace != null && (ty.Namespace.StartsWith("UnityEngine") || ty.Namespace.StartsWith("Il2Cpp"))) { Console.WriteLine($"UNITY TYPE in JSON: {path} ({ty.FullName})"); bad++; return; }
      if (ty.IsGenericType) { foreach (var g in ty.GetGenericArguments()) Walk(g, path + "<>", depth + 1); return; }
      bool Ign(MemberInfo m) { try { return m.CustomAttributes.Any(a => a.AttributeType.Name == "JsonIgnoreAttribute"); } catch { return false; } }
      foreach (var f in ty.GetFields(BindingFlags.Public | BindingFlags.Instance)) { if (Ign(f)) continue; Type ft; try { ft = f.FieldType; } catch (Exception e) { Console.WriteLine($"UNLOADABLE {path}.{f.Name}: {e.Message}"); bad++; continue; } Walk(ft, path + "." + f.Name, depth + 1); }
      foreach (var p in ty.GetProperties(BindingFlags.Public | BindingFlags.Instance)) { if (Ign(p)) continue; Type pt; try { pt = p.PropertyType; } catch (Exception e) { Console.WriteLine($"UNLOADABLE {path}.{p.Name}: {e.Message}"); bad++; continue; } Walk(pt, path + "." + p.Name, depth + 1); }
    }
    Walk(t, name, 0);
    Console.WriteLine("checked " + name);
  }
  return bad;
}}
