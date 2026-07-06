import re

path = "src/Models/DiagramDataGenerator.cs"
with open(path, 'r') as f:
    content = f.read()

content = content.replace("HorizontalAlignment = OxyPlot.HorizontalAlignment.Center,", "TextHorizontalAlignment = OxyPlot.HorizontalAlignment.Center,")
content = content.replace("VerticalAlignment = OxyPlot.VerticalAlignment.Bottom,", "TextVerticalAlignment = OxyPlot.VerticalAlignment.Bottom,")
content = content.replace("VerticalAlignment = OxyPlot.VerticalAlignment.Top,", "TextVerticalAlignment = OxyPlot.VerticalAlignment.Top,")

with open(path, 'w') as f:
    f.write(content)
