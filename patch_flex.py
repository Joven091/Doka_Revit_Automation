import re

with open('src/Views/FlexCalculatorWindow.xaml', 'r') as f:
    content = f.read()

content = content.replace(
'''<Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
        </Grid.RowDefinitions>''',
'''<Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
        </Grid.RowDefinitions>'''
)

# Replace the whole Grid content to be safe
grid_content = '''        <Label Content="Slab Thickness (mm):" Grid.Row="0" Grid.Column="0" VerticalAlignment="Center"/>
        <TextBox Text="{Binding SlabThickness, UpdateSourceTrigger=PropertyChanged}" Grid.Row="0" Grid.Column="1" Margin="0,5"/>

        <Label Content="Floor Height (mm):" Grid.Row="1" Grid.Column="0" VerticalAlignment="Center"/>
        <TextBox Text="{Binding FloorHeight, UpdateSourceTrigger=PropertyChanged}" Grid.Row="1" Grid.Column="1" Margin="0,5"/>

        <Label Content="Prop Type:" Grid.Row="2" Grid.Column="0" VerticalAlignment="Center"/>
        <ComboBox ItemsSource="{Binding PropTypeOptions}" SelectedItem="{Binding PropType}" Grid.Row="2" Grid.Column="1" Margin="0,5"/>

        <Label Content="Prop Orientation:" Grid.Row="3" Grid.Column="0" VerticalAlignment="Center"/>
        <ComboBox ItemsSource="{Binding PropOrientationOptions}" SelectedItem="{Binding PropOrientation}" Grid.Row="3" Grid.Column="1" Margin="0,5"/>

        <Label Content="H20 Component:" Grid.Row="4" Grid.Column="0" VerticalAlignment="Center"/>
        <ComboBox ItemsSource="{Binding H20Options}" SelectedItem="{Binding SelectedH20}" Grid.Row="4" Grid.Column="1" Margin="0,5"/>

        <Label Content="Recommended Prop Spacing:" Grid.Row="5" Grid.Column="0" VerticalAlignment="Center"/>
        <TextBlock Text="{Binding RecommendedPropSpacing, StringFormat={}{0} mm}" Grid.Row="5" Grid.Column="1" Margin="0,5" VerticalAlignment="Center" FontWeight="Bold" Foreground="Gray"/>

        <Label Content="Actual Prop Spacing (mm):" Grid.Row="6" Grid.Column="0" VerticalAlignment="Center"/>
        <TextBox Text="{Binding PropSpacing, UpdateSourceTrigger=PropertyChanged}" Grid.Row="6" Grid.Column="1" Margin="0,5"/>

        <TextBlock Text="{Binding WarningMessage}" Grid.Row="7" Grid.ColumnSpan="2" Foreground="Red" TextWrapping="Wrap" Margin="0,5" Height="40"/>

        <Border Grid.Row="8" Grid.ColumnSpan="2" Background="#EEE" Padding="10" Margin="0,10,0,0" CornerRadius="5">
            <StackPanel Orientation="Horizontal" HorizontalAlignment="Center">
                <Label Content="Calculated Primary Beam Spacing:" FontWeight="Bold"/>
                <TextBlock Text="{Binding PrimaryBeamSpacing, StringFormat={}{0} mm}" FontWeight="Bold" FontSize="16" Foreground="DarkBlue" VerticalAlignment="Center" Margin="10,0,0,0"/>
            </StackPanel>
        </Border>'''

# regex replace everything from <Label Content="Slab Thickness (mm):" ... to </Border>
pattern = r'<Label Content="Slab Thickness \(mm\):"(.|\n)*</Border>'

content = re.sub(pattern, grid_content, content)
content = content.replace('Height="350"', 'Height="450"')

with open('src/Views/FlexCalculatorWindow.xaml', 'w') as f:
    f.write(content)
