namespace SitAndDoStuff.Targeting
{
    // Everything this mod can find and interact with while sitting. ModConfig has a matching
    // CategorySettings (Enabled/Range) for each of these.
    public enum InteractionCategory
    {
        TV,
        Telephone,
        ArcadeMachine,
        SewingMachine,
        FarmComputer,
        MiniJukebox,
        Workbench,
        NPC,          // talking or gifting a villager
        Pet,          // petting a cat/dog/turtle
        FarmAnimal,   // petting a cow/chicken/etc.
        SaloonBar     // ordering food/drinks from Gus
    }
}
