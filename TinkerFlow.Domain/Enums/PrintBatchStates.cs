namespace TinkerFlow.Domain.Enums;

public enum PrintBatchState
{
    Pending = 0,
    Printing = 1,
    ReadyForCollection = 2,
    Completed = 3,
    NoPrints = 4
}