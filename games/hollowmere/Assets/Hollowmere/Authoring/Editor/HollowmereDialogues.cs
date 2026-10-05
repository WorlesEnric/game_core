// Hollowmere - the six conversations of "The Drowned Bell" (P3.1). Each builder returns the whole graph; NPC lines
// change once the shrine is lit (Maren, Hale, Odd and the Echo branch on ShrineLit), and the Echo's lines change once
// the bell has rung. Line keys name the generated voice clips (Media/Voices/<graph>_<key>.wav).
#nullable enable
using static Hollowmere.Authoring.HollowmerePaths;

namespace Hollowmere.Authoring
{
    /// <summary>The Hollowmere dialogue graphs.</summary>
    public static class HollowmereDialogues
    {
        public static readonly string[] All = { "Maren", "Hale", "Odd", "Pip", "BelfryEcho", "Bram" };

        public static GraphBuilder Build(string graph, bool voices)
        {
            switch (graph)
            {
                case "Maren": return Maren(voices);
                case "Hale": return Hale(voices);
                case "Odd": return Odd(voices);
                case "Pip": return Pip(voices);
                case "BelfryEcho": return Echo(voices);
                default: return Bram(voices);
            }
        }

        /// <summary>Healer Maren, the quest giver.</summary>
        public static GraphBuilder Maren(bool voices)
        {
            var g = new GraphBuilder("Maren", "Maren", voices);
            int rung = g.Branch(Condition("BellRung"));
            int heard = g.Branch(Condition("HeardRumour"));
            g.Else(rung, heard);
            int greet = g.Line("greet", "Traveller, you look half drowned yourself. Sit a moment. I am Maren, the healer here.");
            g.Else(heard, greet);
            int rumour = g.Line("rumour", "Since the flood the bell in the old belfry has been silent, and the marsh has gone dark and hungry.");
            g.Next(greet, rumour);
            int intro = g.Action(Action("MarenIntro"));
            g.Next(rumour, intro);
            int ask = g.Choice("Will you go to the belfry?",
                ("How do I cross the marsh?", null),
                ("Where would I find a lantern?", null),
                ("I will go.", null));
            g.Next(intro, ask);
            int cross = g.Line("cross", "Warden Hale keeps the causeway gate and lets no one into Blackmere without a light. Past it, Odd's ferry, or the old punt by the jetty.");
            g.Option(ask, 0, cross);
            int lantern = g.Line("lantern", "Bram sells them at the inn. There was one in the barn once. And a clever hand can make one at the tinker's bench: an oil flask, and dried herbs for a wick.");
            g.Option(ask, 1, lantern);
            int go = g.Line("go", "Then go with care. The marsh keeps what it takes.");
            g.Option(ask, 2, go);
            int lit = g.Branch(Condition("ShrineLit"));
            g.Next(heard, lit);
            g.Next(lit, g.Line("lit", "The shrine is lit? I felt it tonight, like a held breath let go."));
            g.Else(lit, g.Line("again", "Find a lantern, then the causeway. The bell is waiting."));
            g.Next(rung, g.Line("rung", "The bell rang. Hollowmere can sleep again. Thank you, traveller."));
            return g;
        }

        /// <summary>Warden Hale at the causeway gate (opens it for anyone carrying a lantern).</summary>
        public static GraphBuilder Hale(bool voices)
        {
            var g = new GraphBuilder("Hale", "Hale", voices);
            int open = g.Branch(Condition("GateOpen"));
            int light = g.Branch(Condition("HasLantern"));
            g.Else(open, light);
            g.Else(light, g.Line("halt", "Halt. Blackmere eats folk who walk it dark. Come back with a lantern and I will open the gate."));
            int pass = g.Line("pass", "A lantern, and lit. Fair enough. Mind the sinkholes: anything that glints out there is bait.");
            g.Next(light, pass);
            int opens = g.Action(Action("OpenGate"));
            g.Next(pass, opens);
            int lit = g.Branch(Condition("ShrineLit"));
            g.Next(open, lit);
            g.Next(lit, g.Line("lit", "The shrine burns again. First night in years I have not felt watched out here."));
            g.Else(lit, g.Line("open", "The gate is open. Odd ferries from the jetty, if he likes you."));
            return g;
        }

        /// <summary>Ferryman Odd at the marsh jetty (favour after the shrine is lit).</summary>
        public static GraphBuilder Odd(bool voices)
        {
            var g = new GraphBuilder("Odd", "Odd", voices);
            int favour = g.Branch(Condition("OddFavour"));
            int lit = g.Branch(Condition("ShrineLit"));
            g.Else(favour, lit);
            g.Else(lit, g.Line("refuse", "The belfry? I ferry no strangers over that water. Light the sunken shrine, west to east, and we will talk. Or patch the old punt yourself; it wants pitch from an oil flask."));
            int thanks = g.Line("thanks", "You lit the old shrine. My mother tended it. I owe you a crossing.");
            g.Next(lit, thanks);
            int grant = g.Action(Action("OddGrantsFavour"));
            g.Next(thanks, grant);
            int ferry = g.Choice("The black water is calm tonight.", ("Take me to the belfry.", Condition("HasLantern")), ("Not yet.", null));
            g.Next(grant, ferry);
            g.Next(favour, ferry);
            int cross = g.Action(Action("FerryCrossing"));
            g.Option(ferry, 0, cross);
            g.Option(ferry, 1, g.Line("wait", "I will be here. The water is patient."));
            return g;
        }

        /// <summary>Pip, the child with side information (the clapper and the shrine order).</summary>
        public static GraphBuilder Pip(bool voices)
        {
            var g = new GraphBuilder("Pip", "Pip", voices);
            int hello = g.Line("hello", "Did you ever hear the bell? Gran says it rang every dusk before the flood.");
            int ask = g.Choice(string.Empty, ("Where is the clapper?", null), ("Anything about the shrine?", null), ("Bye, Pip.", null));
            g.Next(hello, ask);
            int asked = g.Action(Action("PipAsked"));
            g.Option(ask, 0, asked);
            g.Next(asked, g.Line("clapper", "It fell in the marsh, by the dead willow. Hale will not let me look."));
            g.Option(ask, 1, g.Line("shrine", "Gran says you light the shrine lanterns the way the sun walks: from the willow in the west to the water in the east."));
            return g;
        }

        /// <summary>The Belfry Echo, the ending speaker.</summary>
        public static GraphBuilder Echo(bool voices)
        {
            var g = new GraphBuilder("BelfryEcho", "Belfry Echo", voices);
            int rung = g.Branch(Condition("BellRung"));
            int ended = g.Branch(Condition("EndingReached"));
            g.Else(rung, ended);
            int who = g.Line("who", "...who comes... to the drowned bell...");
            g.Else(ended, who);
            g.Next(ended, g.Line("rest", "...silence... yes... rest now..."));
            int ask = g.Choice("Will you wake it?", ("I will ring it.", null), ("Let it sleep. Let the marsh keep its silence.", null));
            g.Next(who, ask);
            g.Option(ask, 0, g.Line("ring", "...then find its tongue... the clapper the water stole..."));
            int silence = g.Action(Action("SilenceBell"));
            g.Option(ask, 1, silence);
            g.Next(silence, g.Line("sleep", "...silence... yes... rest... the marsh will forget us..."));
            int lit = g.Branch(Condition("ShrineLit"));
            g.Next(rung, lit);
            g.Next(lit, g.Line("free", "The toll... and the shrine's light... I can see the way out. I am free."));
            g.Else(lit, g.Line("stay", "The toll... it hurts... but the village will sleep. I will stay with the bell."));
            return g;
        }

        /// <summary>Bram the innkeeper (the inn vendor).</summary>
        public static GraphBuilder Bram(bool voices)
        {
            var g = new GraphBuilder("Bram", "Bram", voices);
            int hello = g.Line("hello", "Welcome to the Drowned Lantern. Coin for goods, and no questions.");
            int ask = g.Choice("What will it be?",
                ("A lantern (4 coins)", Condition("HasCoins4")),
                ("An oil flask (2 coins)", Condition("HasCoins2")),
                ("Marsh herbs (1 coin)", Condition("HasCoins1")),
                ("Nothing, thanks.", null));
            g.Next(hello, ask);
            int thanks = g.Line("thanks", "There you are. Keep it dry.");
            int lantern = g.Action(Action("BuyLantern"));
            g.Option(ask, 0, lantern);
            g.Next(lantern, thanks);
            int oil = g.Action(Action("BuyOil"));
            g.Option(ask, 1, oil);
            g.Next(oil, thanks);
            int herbs = g.Action(Action("BuyHerbs"));
            g.Option(ask, 2, herbs);
            g.Next(herbs, g.Line("herbs", "Chew them when your legs give out."));
            g.Option(ask, 3, g.Line("bye", "Suit yourself."));
            return g;
        }
    }
}
