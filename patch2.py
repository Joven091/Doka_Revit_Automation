import re

path = "src/ViewModels/DiagramWindowViewModel.cs"
with open(path, 'r') as f:
    content = f.read()

# Update signature
content = content.replace(
    "(PlotModel shear, PlotModel moment, PlotModel deflection,",
    "(PlotModel load, PlotModel shear, PlotModel moment, PlotModel deflection,"
)

# Add to DiagramRows
new_item = """            DiagramRows = new ObservableCollection<DiagramRowItem>
            {
                // 0. Loading Diagram Data
                new DiagramRowItem
                {
                    Title = "Loading Diagram",
                    ChartModel = backendData.load,
                    MaxValueText = "", // No max/min text for loading diagram
                    MinValueText = ""
                },

                // 1. Shear Data"""

content = content.replace("            DiagramRows = new ObservableCollection<DiagramRowItem>\n            {\n                // 1. Shear Data", new_item)

with open(path, 'w') as f:
    f.write(content)
