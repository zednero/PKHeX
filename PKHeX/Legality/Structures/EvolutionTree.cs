﻿using System;
using System.Collections.Generic;
using System.Linq;

namespace PKHeX.Core
{
    public class EvolutionTree
    {
        private List<EvolutionSet> Entries { get; } = new List<EvolutionSet>();
        private readonly EvolutionLineage[] Lineage;
        private readonly GameVersion Game;
        private readonly PersonalTable Personal;
        private readonly int MaxSpeciesTree;

        public EvolutionTree(byte[][] data, GameVersion game, PersonalTable personal, int maxSpeciesTree)
        {
            Game = game;
            Personal = personal;
            MaxSpeciesTree = maxSpeciesTree;
            switch (game)
            {
                case GameVersion.SM:
                    Entries.AddRange(data.Select(d => new EvolutionSet7(d)));
                    break;
                case GameVersion.ORAS:
                    Entries.AddRange(data.Select(d => new EvolutionSet6(d)));
                    break;
            }
            
            // Create Lineages
            Lineage = new EvolutionLineage[Entries.Count];
            for (int i = 0; i < Entries.Count; i++)
                Lineage[i] = new EvolutionLineage();
            if (Game == GameVersion.ORAS)
                Array.Resize(ref Lineage, maxSpeciesTree + 1);

            // Populate Lineages
            for (int i = 1; i < Lineage.Length; i++)
            {
                // Iterate over all possible evolutions
                var s = Entries[i];
                foreach (EvolutionMethod evo in s.PossibleEvolutions)
                {
                    int index = getIndex(evo);
                    if (index < 0)
                        continue;

                    var sourceEvo = evo.Copy(i);

                    Lineage[index].Insert(sourceEvo);
                    // If current entries has a pre-evolution, propagate to evolution as well
                    if (Lineage[i].Chain.Count > 0)
                        Lineage[index].Insert(Lineage[i].Chain[0]);

                    if (index >= i) continue;
                    // If destination species evolves into something (ie a 'baby' Pokemon like Cleffa)
                    // Add it to the corresponding parent chains
                    foreach (EvolutionMethod mid in Entries[index].PossibleEvolutions)
                    {
                        int newIndex = getIndex(mid);
                        if (newIndex < 0)
                            continue;

                        Lineage[newIndex].Insert(sourceEvo);
                    }
                }
            }
            fixEvoTreeManually();
        }

        // There's always oddballs.
        private void fixEvoTreeManually()
        {
            switch (Game)
            {
                case GameVersion.SM:
                    fixEvoTreeSM();
                    break;
                case GameVersion.ORAS:
                    break;
            }
        }
        private void fixEvoTreeSM()
        {
            // Wormadam -- Copy Burmy 0 to Wormadam-1/2
            Lineage[Personal.getFormeIndex(413, 1)].Chain.Insert(0, Lineage[413].Chain[0]);
            Lineage[Personal.getFormeIndex(413, 2)].Chain.Insert(0, Lineage[413].Chain[0]);

            // Shellos -- Move Shellos-1 evo from Gastrodon-0 to Gastrodon-1
            Lineage[Personal.getFormeIndex(422 + 1, 1)].Chain.Insert(0, Lineage[422 + 1].Chain[0]);
            Lineage[422+1].Chain.RemoveAt(0);

            // Flabébé -- Doesn't contain evo info for forms 1-4, copy.
            var fbb = Lineage[669+1].Chain[0];
            for (int i = 1; i <= 4; i++) // NOT AZ
            {
                Lineage[Personal.getFormeIndex(669+1, i)].Chain.Insert(0, fbb);
                Lineage[Personal.getFormeIndex(669+2, i)].Chain.Insert(0, fbb);
            }

            // Scatterbug/Spewpa
            for (int i = 1; i < 18; i++)
                Lineage[Personal.getFormeIndex(666, i)].Chain.InsertRange(0, Lineage[665].Chain);

            // Gourgeist -- Sizes are still relevant. Formes are in reverse order.
            for (int i = 1; i <= 3; i++)
            {
                Lineage[Personal.getFormeIndex(711, i)].Chain.Clear();
                Lineage[Personal.getFormeIndex(711, i)].Chain.Add(Lineage[711].Chain[3-i]);
            }
            Lineage[711].Chain.RemoveRange(0, 3);

            // Add past gen evolutions for other Marowak and Exeggutor
            var raichu1 = Lineage[Personal.getFormeIndex(26, 1)];
            var evo1 = raichu1.Chain[0].StageEntryMethods[0].Copy();
            Lineage[26].Chain.Add(new EvolutionStage { StageEntryMethods = new List<EvolutionMethod> { evo1 } });
            var evo2 = raichu1.Chain[1].StageEntryMethods[0].Copy();
            evo2.Form = -1; evo2.Banlist = new[] { GameVersion.SN, GameVersion.MN };
            Lineage[26].Chain.Add(new EvolutionStage { StageEntryMethods = new List<EvolutionMethod> { evo2 } });

            var exegg = Lineage[Personal.getFormeIndex(103, 1)].Chain[0].StageEntryMethods[0].Copy();
            exegg.Form = -1; exegg.Banlist = new[] { GameVersion.SN, GameVersion.MN }; exegg.Method = 4; // No night required (doesn't matter)
            Lineage[103].Chain.Add(new EvolutionStage { StageEntryMethods = new List<EvolutionMethod> { exegg } });

            var marowak = Lineage[Personal.getFormeIndex(105, 1)].Chain[0].StageEntryMethods[0].Copy();
            marowak.Form = -1; marowak.Banlist = new[] {GameVersion.SN, GameVersion.MN};
            Lineage[105].Chain.Add(new EvolutionStage { StageEntryMethods = new List<EvolutionMethod> { marowak } });
        }

        private int getIndex(PKM pkm)
        {
            if (pkm.Format < 7)
                return pkm.Species;

            var form = pkm.Species == 678 ? 0 : pkm.AltForm; // override Meowstic forme index
            return Personal.getFormeIndex(pkm.Species, form);
        }
        private int getIndex(EvolutionMethod evo)
        {
            int evolvesToSpecies = evo.Species;
            if (evolvesToSpecies == 0)
                return -1;

            if (Personal == null)
                return evolvesToSpecies;

            int evolvesToForm = evo.Form;
            if (evolvesToForm < 0)
                evolvesToForm = 0;

            return Personal.getFormeIndex(evolvesToSpecies, evolvesToForm);
        }
        public IEnumerable<DexLevel> getValidPreEvolutions(PKM pkm, int lvl, bool skipChecks = false)
        {
            int index = getIndex(pkm);
            int maxSpeciesOrigin = Legal.getMaxSpeciesOrigin(pkm);
            return Lineage[index].getExplicitLineage(pkm, lvl, skipChecks, MaxSpeciesTree, maxSpeciesOrigin);
        }
    }

    public abstract class EvolutionSet
    {
        public EvolutionMethod[] PossibleEvolutions;
    }
    public class EvolutionSet6 : EvolutionSet
    {
        private const int SIZE = 6;
        public EvolutionSet6(byte[] data)
        {
            if (data.Length < SIZE || data.Length % SIZE != 0) return;
            int[] argEvos = {6, 8, 16, 17, 18, 19, 20, 21, 22, 29, 30, 31, 32, 33, 34};
            PossibleEvolutions = new EvolutionMethod[data.Length / SIZE];
            for (int i = 0; i < data.Length; i += SIZE)
            {
                PossibleEvolutions[i/SIZE] = new EvolutionMethod
                {
                    Method = BitConverter.ToUInt16(data, i + 0),
                    Argument = BitConverter.ToUInt16(data, i + 2),
                    Species = BitConverter.ToUInt16(data, i + 4),

                    // Copy
                    Level = BitConverter.ToUInt16(data, i + 2),
                };

                // Argument is used by both Level argument and Item/Move/etc. Clear if appropriate.
                if (argEvos.Contains(PossibleEvolutions[i/SIZE].Method))
                    PossibleEvolutions[i/SIZE].Level = 0;
            }
        }
    }
    public class EvolutionSet7 : EvolutionSet
    {
        private const int SIZE = 8;
        public EvolutionSet7(byte[] data)
        {
            if (data.Length < SIZE || data.Length % SIZE != 0) return;
            PossibleEvolutions = new EvolutionMethod[data.Length / SIZE];
            for (int i = 0; i < data.Length; i += SIZE)
            {
                PossibleEvolutions[i / SIZE] = new EvolutionMethod
                {
                    Method = BitConverter.ToUInt16(data, i + 0),
                    Argument = BitConverter.ToUInt16(data, i + 2),
                    Species = BitConverter.ToUInt16(data, i + 4),
                    Form = (sbyte)data[i + 6],
                    Level = data[i + 7],
                };
            }
        }
    }
    public class EvolutionMethod
    {
        public int Method;
        public int Species;
        public int Argument;
        public int Form = -1;
        public int Level;

        public bool RequiresLevelUp;
        public static readonly int[] TradeMethods = {5, 6, 7};
        public GameVersion[] Banlist = new GameVersion[0];

        public bool Valid(PKM pkm, int lvl, bool skipChecks)
        {
            RequiresLevelUp = false;
            if (Form > -1)
                if (!skipChecks && pkm.AltForm != Form)
                    return false;

            if (!skipChecks && Banlist.Contains((GameVersion)pkm.Version))
                return false;

            switch (Method)
            {
                case 8: // Use Item
                    return true;
                case 17: // Male
                    return pkm.Gender == 0;
                case 18: // Female
                    return pkm.Gender == 1;

                case 5: // Trade Evolution
                case 6: // Trade while Holding
                case 7: // Trade for Opposite Species
                    return !pkm.IsUntraded || skipChecks;
                
                    // Special Levelup Cases
                case 16:
                    if (pkm.CNT_Beauty < Argument)
                        return false;
                    goto default;
                case 23: // Gender = Male
                    if (pkm.Gender != 0)
                        return false;
                    goto default;
                case 24: // Gender = Female
                    if (pkm.Gender != 1)
                        return false;
                    goto default;
                case 34: // Gender = Female, out Form1
                    if (pkm.Gender != 1 || pkm.AltForm != 1)
                        return false;
                    goto default;

                case 36: // Any Time on Version
                case 37: // Daytime on Version
                case 38: // Nighttime on Version
                    if (pkm.Version != Argument && pkm.IsUntraded || skipChecks)
                        return skipChecks;
                    goto default;

                default:
                    if (Level == 0 && lvl < 2)
                        return false;
                    if (lvl < Level)
                        return false;

                    RequiresLevelUp = true;

                    // Check Met Level for extra validity
                    switch (pkm.GenNumber)
                    {
                        case 1: // No metdata in RBY
                        case 2: // No metdata in GS, Crystal metdata can be reset
                            return true;
                        case 3:
                        case 4:
                            if (pkm.Format > pkm.GenNumber) // Pal Park / PokeTransfer updates Met Level
                                return true;
                            return pkm.Met_Level < lvl;

                        case 5: // Bank keeps current level
                        case 6:
                        case 7:
                            return lvl >= Level && (!pkm.IsNative || pkm.Met_Level < lvl);
                    }
                    return false;
            }
        }

        public DexLevel GetDexLevel(int species, int lvl)
        {

            return new DexLevel
            {
                Species = species,
                Level = lvl,
                Form = Form,
                Flag = Method,
            };
        }

        public EvolutionMethod Copy(int species = -1)
        {
            if (species < 0)
                species = Species;
            return new EvolutionMethod
            {
                Method = Method,
                Species = species,
                Argument = Argument,
                Form = Form,
                Level = Level
            };
        }
    }

    // Informatics
    public class EvolutionLineage
    {
        public readonly List<EvolutionStage> Chain = new List<EvolutionStage>();

        public void Insert(EvolutionMethod entry)
        {
            int matchChain = -1;
            for (int i = 0; i < Chain.Count; i++)
                if (Chain[i].StageEntryMethods.Any(e => e.Species == entry.Species))
                    matchChain = i;

            if (matchChain != -1)
                Chain[matchChain].StageEntryMethods.Add(entry);
            else
                Chain.Insert(0, new EvolutionStage { StageEntryMethods = new List<EvolutionMethod> {entry}});
        }
        public void Insert(EvolutionStage evo)
        {
            Chain.Insert(0, evo);
        }

        public IEnumerable<DexLevel> getExplicitLineage(PKM pkm, int lvl, bool skipChecks, int maxSpeciesTree, int maxSpeciesOrigin)
        {
            List<DexLevel> dl = new List<DexLevel> { new DexLevel { Species = pkm.Species, Level = lvl, Form = pkm.AltForm } };
            for (int i = Chain.Count-1; i >= 0; i--) // reverse evolution!
            {
                bool oneValid = false;
                foreach (var evo in Chain[i].StageEntryMethods)
                {
                    if (!evo.Valid(pkm, lvl, skipChecks))
                        continue;

                    oneValid = true;
                    int species = evo.Species;

                    // Gen7 Personal Formes -- unmap the forme personal entry ID to the actual species ID since species are consecutive
                    if (evo.Species > maxSpeciesTree)
                        species = pkm.Species - Chain.Count + i;

                    dl.Add(evo.GetDexLevel(species, lvl));
                    
                    if (evo.RequiresLevelUp)
                        lvl--;
                    break;
                }
                if (!oneValid)
                    break;
            }

            // Remove future gen preevolutions, no munchlax in a gen3 snorlax, no pichu in a gen1 vc raichu, etc
            if (dl.Any(d => d.Species <= maxSpeciesOrigin) && dl.Last().Species > maxSpeciesOrigin)
                dl.RemoveAt(dl.Count - 1); 

            return dl;
        }
    }
    public class EvolutionStage
    {
        public List<EvolutionMethod> StageEntryMethods;
    }
}
