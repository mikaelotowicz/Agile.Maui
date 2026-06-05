// Platforms/Windows/VirtualizedCollectionView/VirtualizedCollectionViewHandler.cs
//
// Handler customizado não é necessário no Windows. VirtualizedCollectionView herda de
// ContentView e define Content = CollectionView do MAUI em seu construtor (#if WINDOWS),
// utilizando o ContentViewHandler padrão do MAUI para renderização.
