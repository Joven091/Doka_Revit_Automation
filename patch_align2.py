import re

path = "src/Models/DiagramDataGenerator.cs"
with open(path, 'r') as f:
    content = f.read()

# Let's adjust OxyThickness to use 70 for left margin just to be safe with y-axis labels.
content = content.replace("new OxyThickness(50, 20, 20, 40)", "new OxyThickness(70, 30, 20, 40)")

with open(path, 'w') as f:
    f.write(content)
