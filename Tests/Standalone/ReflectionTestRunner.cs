using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
public static class ProductionRunner {
 public static int Main() {
  int passed=0,failed=0;
  foreach(var type in Assembly.GetExecutingAssembly().GetTypes().OrderBy(t=>t.FullName))
  foreach(var method in type.GetMethods()) {
   var cases=method.GetCustomAttributes(typeof(TestCaseAttribute),false).Cast<TestCaseAttribute>().ToArray();
   if(cases.Length==0 && !Attribute.IsDefined(method,typeof(TestAttribute))) continue;
   var inputs=cases.Length>0 ? cases.Select(c=>c.Arguments).ToArray() : new[]{new object[0]};
   foreach(var args in inputs) {
    string name=type.Name+"."+method.Name+(args.Length>0 ? "("+string.Join(",",args)+")":"");
    try { var result=method.Invoke(Activator.CreateInstance(type),args); if(result is Task task)task.GetAwaiter().GetResult(); Console.WriteLine("PASS "+name); passed++; }
    catch(Exception ex) { Console.WriteLine("FAIL "+name+" "+(ex.InnerException??ex)); failed++; }
   }
  }
  Console.WriteLine("RESULT "+passed+" passed, "+failed+" failed");return failed==0?0:1;
 }
}
