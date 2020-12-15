using System;

namespace DinoDips
{
    class Program
    {
        public static void Main(string[] args)
        {
            VeldridStartupWindow window = new VeldridStartupWindow("DinoDIPS!");
            DipsGame dipsGame = new DipsGame(window);
            window.Run();
        }
    }
}