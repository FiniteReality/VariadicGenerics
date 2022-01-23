// See https://aka.ms/new-console-template for more information

using VariadicGenerics;
using System;

Stuff stuff = new();
stuff.WriteMultiple<string, string>("oh", "cool");

class Stuff
{
    public void WriteMultiple<[Variadic] T>(T value)
        where T : class
    {
        Console.WriteLine(value);
    }
}

class VariadicAttribute : Attribute { }