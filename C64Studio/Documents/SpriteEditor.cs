﻿using C64Studio.CustomRenderer;
using C64Studio.Displayer;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Windows.Forms;
using WeifenLuo.WinFormsUI.Docking;
using C64Studio.Formats;
using GR.Memory;
using C64Studio.Types;
using C64Studio.Converter;

namespace C64Studio
{
  public partial class SpriteEditor : BaseDocument
  {
    private enum ColorType
    {
      BACKGROUND = 0,
      MULTICOLOR_1,
      MULTICOLOR_2,
      SPRITE_COLOR
    }


    GR.Image.FastImage fastImage1 = new GR.Image.FastImage();

    private int                         m_CurrentSprite = 0;
    private ColorType                   m_CurrentColorType = ColorType.SPRITE_COLOR;

    private string                      m_ImportError = "";

    private bool                        m_IsSpriteProject = true;

    private Formats.SpriteProject       m_SpriteProject = new C64Studio.Formats.SpriteProject();

    private Formats.SpriteProject.Layer m_CurrentLayer = null;

    private bool                        m_ButtonReleased = false;
    private bool                        m_RButtonReleased = false;

    private Timer                       m_AnimTimer = new Timer();

    private int                         m_AnimFramePos = 0;
    private int                         m_AnimFrameTicks = 0;



    public SpriteEditor( StudioCore Core )
    {
      this.Core = Core;
      DocumentInfo.Type = ProjectElement.ElementType.SPRITE_SET;
      DocumentInfo.UndoManager.MainForm = Core.MainForm;

      m_IsSaveable = true;
      InitializeComponent();

      GR.Image.DPIHandler.ResizeControlsForDPI( this );

      listLayers.ItemAdded += new ArrangedItemList.ItemModifiedEventHandler( listLayers_ItemAdded );

      pictureEditor.DisplayPage.Create( 24, 21, System.Drawing.Imaging.PixelFormat.Format8bppIndexed );
      layerPreview.DisplayPage.Create( 320, 200, System.Drawing.Imaging.PixelFormat.Format8bppIndexed );
      panelSprites.PixelFormat = System.Drawing.Imaging.PixelFormat.Format8bppIndexed;
      panelSprites.SetDisplaySize( 4 * 24, 6 * 21 );
      panelSprites.ClientSize = new System.Drawing.Size( 4 * 24 * 2 + System.Windows.Forms.SystemInformation.VerticalScrollBarWidth, 6 * 21 * 2 );

      CustomRenderer.PaletteManager.ApplyPalette( pictureEditor.DisplayPage );
      CustomRenderer.PaletteManager.ApplyPalette( panelSprites.DisplayPage );
      CustomRenderer.PaletteManager.ApplyPalette( layerPreview.DisplayPage );

      for ( int i = 0; i < m_SpriteProject.NumSprites; ++i )
      {
        m_SpriteProject.Sprites.Add( new C64Studio.Formats.SpriteProject.SpriteData() );
        panelSprites.Items.Add( i.ToString(), m_SpriteProject.Sprites[i].Image );
        CustomRenderer.PaletteManager.ApplyPalette( m_SpriteProject.Sprites[i].Image );
        comboSprite.Items.Add( i );
      }

      for ( int i = 0; i < 16; ++i )
      {
        comboBackground.Items.Add( i.ToString( "d2" ) );
        comboMulticolor1.Items.Add( i.ToString( "d2" ) );
        comboMulticolor2.Items.Add( i.ToString( "d2" ) );
        comboSpriteColor.Items.Add( i.ToString( "d2" ) );
        comboLayerColor.Items.Add( i.ToString( "d2" ) );
        comboLayerBGColor.Items.Add( i.ToString( "d2" ) );
      }
      comboBackground.SelectedIndex = 0;
      comboMulticolor1.SelectedIndex = 0;
      comboMulticolor2.SelectedIndex = 0;
      comboSpriteColor.SelectedIndex = 1;
      comboLayerColor.SelectedIndex = 1;
      comboLayerBGColor.SelectedIndex = 0;
      comboSprite.SelectedIndex = 0;

      radioSpriteColor.Checked = true;
      pictureEditor.SetImageSize( 24, 21 );
      panelSprites.SelectedIndex = 0;

      pictureEditor.PostPaint += new GR.Forms.FastPictureBox.PostPaintCallback( pictureEditor_PostPaint );

      checkExportToDataIncludeRes.Checked = true;
      checkExportToDataWrap.Checked = true;
      comboExportRange.Items.Add( "All" );
      comboExportRange.Items.Add( "Selection" );
      comboExportRange.Items.Add( "Range" );
      comboExportRange.SelectedIndex = 0;

      panelSprites.KeyDown += new KeyEventHandler( HandleKeyDown );
      pictureEditor.PreviewKeyDown += new PreviewKeyDownEventHandler( pictureEditor_PreviewKeyDown );

      RebuildSpriteImage( m_CurrentSprite );

      m_CurrentLayer = new Formats.SpriteProject.Layer();
      m_CurrentLayer.Name = "Default";
      m_SpriteProject.SpriteLayers.Add( m_CurrentLayer );
      foreach ( var layer in m_SpriteProject.SpriteLayers )
      {
        ArrangedItemEntry item = new ArrangedItemEntry( layer.Name );
        item.Tag = layer;
        listLayers.Items.Add( item );
      }

      labelCharNo.Text = "Sprite: " + m_CurrentSprite.ToString();
      comboSpriteColor.SelectedIndex = m_SpriteProject.Sprites[m_CurrentSprite].Color;
      checkMulticolor.Checked = m_SpriteProject.Sprites[m_CurrentSprite].Multicolor;
      pictureEditor.Image = m_SpriteProject.Sprites[m_CurrentSprite].Image;

      panelCharacters_SelectedIndexChanged( null, null );

      RefreshDisplayOptions();

      m_AnimTimer.Tick += animTimer_Tick;
    }



    private void animTimer_Tick( object sender, EventArgs e )
    {
      if ( m_AnimFramePos >= m_SpriteProject.SpriteLayers.Count )
      {
        m_AnimFramePos = 0;
        m_AnimFrameTicks = 0;
        listLayers.SelectedIndex = m_AnimFramePos;
      }
      m_AnimFrameTicks += 100;
      if ( m_AnimFrameTicks >= m_CurrentLayer.DelayMS )
      {
        m_AnimFrameTicks -= m_CurrentLayer.DelayMS;
        m_AnimFramePos = ( m_AnimFramePos + 1 ) % m_SpriteProject.SpriteLayers.Count;
        listLayers.SelectedIndex = m_AnimFramePos;
      }
    }



    void pictureEditor_PreviewKeyDown( object sender, PreviewKeyDownEventArgs e )
    {
      KeyEventArgs ke = new KeyEventArgs( e.KeyData );
      HandleKeyDown( sender, ke );
    }



    void DrawSpriteImage( GR.Image.MemoryImage Target, GR.Memory.ByteBuffer Data, int BackgroundColor, int CustomColor, bool Multicolor, int MultiColor1, int MultiColor2 )
    {
      if ( !Multicolor )
      {
        // single color
        for ( int j = 0; j < 21; ++j )
        {
          for ( int k = 0; k < 3; ++k )
          {
            for ( int i = 0; i < 8; ++i )
            {
              if ( ( Data.ByteAt( j * 3 + k ) & ( 1 << ( 7 - i ) ) ) != 0 )
              {
                //Data.Image.SetPixel( k * 8 + i, j, m_ColorValues[Data.Color] );
                Target.SetPixel( k * 8 + i, j, (uint)CustomColor );
              }
              else
              {
                //Data.Image.SetPixel( k * 8 + i, j, m_ColorValues[m_BackgroundColor] );
                Target.SetPixel( k * 8 + i, j, (uint)BackgroundColor );
              }
            }
          }
        }
      }
      else
      {
        // multicolor
        for ( int j = 0; j < 21; ++j )
        {
          for ( int k = 0; k < 3; ++k )
          {
            for ( int i = 0; i < 4; ++i )
            {
              int pixelValue = ( Data.ByteAt( j * 3 + k ) & ( 3 << ( ( 3 - i ) * 2 ) ) ) >> ( ( 3 - i ) * 2 );

              switch ( pixelValue )
              {
                case 0:
                  pixelValue = BackgroundColor;
                  break;
                case 1:
                  pixelValue = MultiColor1;
                  break;
                case 3:
                  pixelValue = MultiColor2;
                  break;
                case 2:
                  pixelValue = CustomColor;
                  break;
              }
              Target.SetPixel( k * 8 + i * 2, j, (uint)pixelValue );
              Target.SetPixel( k * 8 + i * 2 + 1, j, (uint)pixelValue );
            }
          }
        }
      }
    }



    void DrawSpriteImage( GR.Image.FastImage Target, int X, int Y, GR.Memory.ByteBuffer Data, int CustomColor, bool Multicolor, int MultiColor1, int MultiColor2, bool ExpandX, bool ExpandY )
    {
      if ( Multicolor )
      {
        SpriteDisplayer.DisplayMultiColorSprite( Data, m_SpriteProject.BackgroundColor, MultiColor1, MultiColor2, CustomColor, Target, X, Y, ExpandX, ExpandY );
      }
      else
      {
        SpriteDisplayer.DisplayHiResSprite( Data, m_SpriteProject.BackgroundColor, CustomColor, Target, X, Y, ExpandX, ExpandY );
      }
    }



    void RebuildSpriteImage( int SpriteIndex )
    {
      var Data = m_SpriteProject.Sprites[SpriteIndex];

      DrawSpriteImage( Data.Image, Data.Data, m_SpriteProject.BackgroundColor, Data.Color, Data.Multicolor, m_SpriteProject.MultiColor1, m_SpriteProject.MultiColor2 );
    }



    private new bool Modified
    {
      get
      {
        return base.Modified;
      }
      set
      {
        if ( value )
        {
          SetModified();
        }
        else
        {
          SetUnmodified();
        }
        saveSpriteProjectToolStripMenuItem.Enabled = Modified;
      }
    }



    void MirrorX()
    {
      var selectedSprites = panelSprites.SelectedIndices;

      bool firstEntry = true;
      foreach ( var spriteIndex in selectedSprites )
      {
        DocumentInfo.UndoManager.AddUndoTask( new Undo.UndoSpritesetSpriteChange( this, m_SpriteProject, spriteIndex ), firstEntry );
        firstEntry = false;
        var sprite = m_SpriteProject.Sprites[spriteIndex];

        for ( int y = 0; y < 21; ++y )
        {
          byte    byte1 = sprite.Data.ByteAt( y * 3 );
          byte    byte2 = sprite.Data.ByteAt( y * 3 + 1 );
          byte    byte3 = sprite.Data.ByteAt( y * 3 + 2 );
          if ( sprite.Multicolor )
          {
            byte2 = (byte)( ( ( byte2 & 0xc0 ) >> 6 ) + ( ( byte2 & 0x30 ) >> 2 ) + ( ( byte2 & 0x0c ) << 2 ) + ( ( byte2 & 0x03 ) << 6 ) );
            byte byteNew1 = (byte)( ( ( byte3 & 0xc0 ) >> 6 ) + ( ( byte3 & 0x30 ) >> 2 ) + ( ( byte3 & 0x0c ) << 2 ) + ( ( byte3 & 0x03 ) << 6 ) );
            byte3 = (byte)( ( ( byte1 & 0xc0 ) >> 6 ) + ( ( byte1 & 0x30 ) >> 2 ) + ( ( byte1 & 0x0c ) << 2 ) + ( ( byte1 & 0x03 ) << 6 ) );
            byte1 = byteNew1;

          }
          else
          {
            byte2 = (byte)( ( ( byte2 & 0x80 ) >> 7 )
                  + ( ( byte2 & 0x40 ) >> 5 )
                  + ( ( byte2 & 0x20 ) >> 3 )
                  + ( ( byte2 & 0x10 ) >> 1 )
                  + ( ( byte2 & 0x08 ) << 1 )
                  + ( ( byte2 & 0x04 ) << 3 )
                  + ( ( byte2 & 0x02 ) << 5 )
                  + ( ( byte2 & 0x01 ) << 7 ) );
            byte byteNew3 = (byte)( ( ( byte1 & 0x80 ) >> 7 )
                  + ( ( byte1 & 0x40 ) >> 5 )
                  + ( ( byte1 & 0x20 ) >> 3 )
                  + ( ( byte1 & 0x10 ) >> 1 )
                  + ( ( byte1 & 0x08 ) << 1 )
                  + ( ( byte1 & 0x04 ) << 3 )
                  + ( ( byte1 & 0x02 ) << 5 )
                  + ( ( byte1 & 0x01 ) << 7 ) );
            byte1 = (byte)( ( ( byte3 & 0x80 ) >> 7 )
                  + ( ( byte3 & 0x40 ) >> 5 )
                  + ( ( byte3 & 0x20 ) >> 3 )
                  + ( ( byte3 & 0x10 ) >> 1 )
                  + ( ( byte3 & 0x08 ) << 1 )
                  + ( ( byte3 & 0x04 ) << 3 )
                  + ( ( byte3 & 0x02 ) << 5 )
                  + ( ( byte3 & 0x01 ) << 7 ) );
            byte3 = byteNew3;
          }
          sprite.Data.SetU8At( y * 3, byte1 );
          sprite.Data.SetU8At( y * 3 + 1, byte2 );
          sprite.Data.SetU8At( y * 3 + 2, byte3 );
        }
        RebuildSpriteImage( spriteIndex );
        panelSprites.InvalidateItemRect( spriteIndex );
      }
      pictureEditor.Invalidate();
      Modified = true;
    }



    void MirrorY()
    {
      var selectedSprites = panelSprites.SelectedIndices;

      bool firstEntry = true;
      foreach ( var spriteIndex in selectedSprites )
      {
        DocumentInfo.UndoManager.AddUndoTask( new Undo.UndoSpritesetSpriteChange( this, m_SpriteProject, spriteIndex ), firstEntry );
        firstEntry = false;
        var sprite = m_SpriteProject.Sprites[spriteIndex];

        for ( int y = 0; y < 10; ++y )
        {
          for ( int x = 0; x < 3; ++x )
          {
            byte oldValue =sprite.Data.ByteAt( y * 3 + x );
            sprite.Data.SetU8At( y * 3 + x, sprite.Data.ByteAt( x + 3 * ( 20 - y ) ) );
            sprite.Data.SetU8At( x + 3 * ( 20 - y ), oldValue );
          }
        }
        RebuildSpriteImage( spriteIndex );
        panelSprites.InvalidateItemRect( spriteIndex );
      }
      pictureEditor.Invalidate();
      Modified = true;
    }



    void HandleKeyDown( object sender, KeyEventArgs e )
    {
      if ( ( e.Modifiers == Keys.Control )
      &&   ( e.KeyCode == Keys.C ) )
      {
        // copy
        CopySpriteToClipboard();
      }
      else if ( ( e.Modifiers == Keys.Control )
      &&        ( e.KeyCode == Keys.V ) )
      {
        PasteFromClipboard();
        if ( m_ImportError.Length > 0 )
        {
          System.Windows.Forms.MessageBox.Show( m_ImportError, "Error while converting" );
        }
      }
    }



    private void comboColor_DrawItem( object sender, DrawItemEventArgs e )
    {
      ComboBox combo = (ComboBox)sender;

      Core.Theming.DrawSingleColorComboBox( combo, e );
    }



    private void comboMulticolor_DrawItem( object sender, DrawItemEventArgs e )
    {
      ComboBox combo = (ComboBox)sender;

      Core.Theming.DrawMultiColorComboBox( combo, e );
    }



    private void pictureEditor_MouseDown( object sender, MouseEventArgs e )
    {
      pictureEditor.Focus();
      HandleMouseOnEditor( e.X, e.Y, e.Button );
    }



    private void HandleMouseOnEditor( int X, int Y, MouseButtons Buttons )
    {
      if ( !pictureEditor.ClientRectangle.Contains( X, Y ) )
      {
        return;
      }

      int     charX = ( X * 24 ) / pictureEditor.ClientRectangle.Width;
      int     charY = ( Y * 21 ) / pictureEditor.ClientRectangle.Height;

      if ( ( Buttons & MouseButtons.Left ) != 0 )
      {
        int newColor = 0;

        int byteX = charX / 8;
        charX %= 8;
        byte    charByte = m_SpriteProject.Sprites[m_CurrentSprite].Data.ByteAt( charY * 3 + byteX );
        byte    newByte = charByte;

        if ( !m_SpriteProject.Sprites[m_CurrentSprite].Multicolor )
        {
          // single color
          charX = 7 - charX;
          if ( m_CurrentColorType == ColorType.SPRITE_COLOR )
          {
            newByte |= (byte)( 1 << charX );
            newColor = m_SpriteProject.Sprites[m_CurrentSprite].Color;
          }
          else
          {
            newByte &= (byte)~( 1 << charX );
            newColor = m_SpriteProject.BackgroundColor;
          }
        }
        else
        {
          // multi color
          charX = ( ( X * 12 ) / pictureEditor.ClientRectangle.Width ) % 4;
          charX = 3 - charX;

          newByte &= (byte)~( 3 << ( 2 * charX ) );

          int     replacementBytes = 0;

          switch ( m_CurrentColorType )
          {
            case ColorType.BACKGROUND:
              newColor = m_SpriteProject.BackgroundColor;
              break;
            case ColorType.SPRITE_COLOR:
              replacementBytes = 2;
              newColor = m_SpriteProject.Sprites[m_CurrentSprite].Color;
              break;
            case ColorType.MULTICOLOR_1:
              replacementBytes = 1;
              newColor = m_SpriteProject.MultiColor1;
              break;
            case ColorType.MULTICOLOR_2:
              replacementBytes = 3;
              newColor = m_SpriteProject.MultiColor2;
              break;
          }
          newByte |= (byte)( replacementBytes << ( 2 * charX ) );
        }
        if ( newByte != charByte )
        {
          Modified = true;
          
          if ( m_ButtonReleased )
          {
            DocumentInfo.UndoManager.AddUndoTask( new Undo.UndoSpritesetSpriteChange( this, m_SpriteProject, m_CurrentSprite ) );
            m_ButtonReleased = false;
          }

          m_SpriteProject.Sprites[m_CurrentSprite].Data.SetU8At( charY * 3 + byteX, newByte );
          if ( m_SpriteProject.Sprites[m_CurrentSprite].Multicolor )
          {
            m_SpriteProject.Sprites[m_CurrentSprite].Image.SetPixel( charX * 2, charY, (uint)newColor );
            m_SpriteProject.Sprites[m_CurrentSprite].Image.SetPixel( charX * 2 + 1, charY, (uint)newColor );
          }
          else
          {
            m_SpriteProject.Sprites[m_CurrentSprite].Image.SetPixel( charX, charY, (uint)newColor );
          }

          SpriteChanged( m_CurrentSprite );
        }
      }
      else
      {
        m_ButtonReleased = true;
      }
      if ( ( Buttons & MouseButtons.Right ) != 0 )
      {
        int xPos = charX;

        int byteX = charX / 8;
        charX %= 8;
        byte charByte = m_SpriteProject.Sprites[m_CurrentSprite].Data.ByteAt( charY * 3 + byteX );
        byte newByte = charByte;

        if ( !m_SpriteProject.Sprites[m_CurrentSprite].Multicolor )
        {
          // single color
          charX = 7 - charX;
          newByte &= (byte)~( 1 << charX );
        }
        else
        {
          // multi color
          charX = ( ( X * 12 ) / pictureEditor.ClientRectangle.Width ) % 4;
          charX = 3 - charX;
          xPos &= 0xfe;

          newByte &= (byte)~( 3 << ( 2 * charX ) );
        }
        if ( newByte != charByte )
        {
          Modified = true;

          if ( m_RButtonReleased )
          {
            m_RButtonReleased = false;
            DocumentInfo.UndoManager.AddUndoTask( new Undo.UndoSpritesetSpriteChange( this, m_SpriteProject, m_CurrentSprite ) );
          }

          m_SpriteProject.Sprites[m_CurrentSprite].Data.SetU8At( charY * 3 + byteX, newByte );

          if ( m_SpriteProject.Sprites[m_CurrentSprite].Multicolor )
          {
            m_SpriteProject.Sprites[m_CurrentSprite].Image.SetPixel( xPos, charY, (uint)m_SpriteProject.BackgroundColor );
            m_SpriteProject.Sprites[m_CurrentSprite].Image.SetPixel( xPos + 1, charY, (uint)m_SpriteProject.BackgroundColor );
          }
          else
          {
            m_SpriteProject.Sprites[m_CurrentSprite].Image.SetPixel( xPos, charY, (uint)m_SpriteProject.BackgroundColor );
          }
          CurrentSpriteModified();
          pictureEditor.Invalidate();
          panelSprites.InvalidateItemRect( m_CurrentSprite );
        }
      }
      else
      {
        m_RButtonReleased = true;
      }
    }



    private void radioBackground_CheckedChanged( object sender, EventArgs e )
    {
      m_CurrentColorType = ColorType.BACKGROUND;
    }



    private void radioMultiColor1_CheckedChanged( object sender, EventArgs e )
    {
      m_CurrentColorType = ColorType.MULTICOLOR_1;
    }



    private void radioMulticolor2_CheckedChanged( object sender, EventArgs e )
    {
      m_CurrentColorType = ColorType.MULTICOLOR_2;
    }



    private void radioCharColor_CheckedChanged( object sender, EventArgs e )
    {
      m_CurrentColorType = ColorType.SPRITE_COLOR;
    }



    private void pictureEditor_MouseMove( object sender, MouseEventArgs e )
    {
      MouseButtons    buttons = e.Button;
      if ( !pictureEditor.Focused )
      {
        buttons = 0;
      }
      HandleMouseOnEditor( e.X, e.Y, buttons );
    }



    private void checkMulticolor_CheckedChanged( object sender, EventArgs e )
    {
      if ( DoNotUpdateFromControls )
      {
        return;
      }
      DocumentInfo.UndoManager.StartUndoGroup();

      var selectedSprites = panelSprites.SelectedIndices;

      foreach ( var i in selectedSprites )
      {
        if ( m_SpriteProject.Sprites[i].Multicolor != checkMulticolor.Checked )
        {
          DocumentInfo.UndoManager.AddGroupedUndoTask( new Undo.UndoSpritesetSpriteChange( this, m_SpriteProject, i ) );

          m_SpriteProject.Sprites[i].Multicolor = checkMulticolor.Checked;
          Modified = true;
          RebuildSpriteImage( i );
          if ( m_CurrentSprite == i )
          {
            pictureEditor.Invalidate();
          }
          panelSprites.InvalidateItemRect( i );
        }
      }
    }



    private void comboBackground_SelectedIndexChanged( object sender, EventArgs e )
    {
      if ( m_SpriteProject.BackgroundColor != comboBackground.SelectedIndex )
      {
        DocumentInfo.UndoManager.AddUndoTask( new Undo.UndoSpritesetValuesChange( this, m_SpriteProject ) );

        m_SpriteProject.BackgroundColor = comboBackground.SelectedIndex;
        Modified = true;
        for ( int i = 0; i < m_SpriteProject.NumSprites; ++i )
        {
          RebuildSpriteImage( i );
        }
        pictureEditor.Invalidate();
        panelSprites.Invalidate();
      }
    }



    private void comboMulticolor1_SelectedIndexChanged( object sender, EventArgs e )
    {
      if ( m_SpriteProject.MultiColor1 != comboMulticolor1.SelectedIndex )
      {
        DocumentInfo.UndoManager.AddUndoTask( new Undo.UndoSpritesetValuesChange( this, m_SpriteProject ) );

        m_SpriteProject.MultiColor1 = comboMulticolor1.SelectedIndex;
        Modified = true;
        for ( int i = 0; i < m_SpriteProject.NumSprites; ++i )
        {
          RebuildSpriteImage( i );
        }
        pictureEditor.Invalidate();
        panelSprites.Invalidate();
      }
    }



    private void comboMulticolor2_SelectedIndexChanged( object sender, EventArgs e )
    {
      if ( m_SpriteProject.MultiColor2 != comboMulticolor2.SelectedIndex )
      {
        DocumentInfo.UndoManager.AddUndoTask( new Undo.UndoSpritesetValuesChange( this, m_SpriteProject ) );

        m_SpriteProject.MultiColor2 = comboMulticolor2.SelectedIndex;
        Modified = true;
        for ( int i = 0; i < m_SpriteProject.NumSprites; ++i )
        {
          RebuildSpriteImage( i );
        }
        pictureEditor.Invalidate();
        panelSprites.Invalidate();
      }
    }



    private void comboSpriteColor_SelectedIndexChanged( object sender, EventArgs e )
    {
      if ( DoNotUpdateFromControls )
      {
        return;
      }
      DocumentInfo.UndoManager.StartUndoGroup();

      var selectedSprites = panelSprites.SelectedIndices;

      foreach ( var i in selectedSprites )
      {
        if ( m_SpriteProject.Sprites[i].Color != comboSpriteColor.SelectedIndex )
        {
          DocumentInfo.UndoManager.AddGroupedUndoTask( new Undo.UndoSpritesetSpriteChange( this, m_SpriteProject, i ) );
          m_SpriteProject.Sprites[i].Color = comboSpriteColor.SelectedIndex;
          Modified = true;
          RebuildSpriteImage( i );
          if ( i == m_CurrentSprite )
          {
            pictureEditor.Invalidate();
          }
          panelSprites.InvalidateItemRect( i );
        }
      }
    }



    private void panelCharacters_SelectedIndexChanged( object sender, EventArgs e )
    {
      int newChar = panelSprites.SelectedIndex;
      if ( newChar != -1 )
      {
        labelCharNo.Text = "Sprite: " + newChar.ToString();
        m_CurrentSprite = newChar;

        DoNotUpdateFromControls = true;
        comboSpriteColor.SelectedIndex  = m_SpriteProject.Sprites[m_CurrentSprite].Color;
        checkMulticolor.Checked       = m_SpriteProject.Sprites[m_CurrentSprite].Multicolor;
        DoNotUpdateFromControls = false;

        pictureEditor.Image = m_SpriteProject.Sprites[m_CurrentSprite].Image;
      }
      btnDeleteSprite.Enabled = ( panelSprites.SelectedIndex != -1 );
    }



    private void openToolStripMenuItem_Click( object sender, EventArgs e )
    {
      string filename;

      if ( OpenFile( "Open Sprite Project or File", Types.Constants.FILEFILTER_SPRITE + Types.Constants.FILEFILTER_SPRITE_SPRITEPAD + Types.Constants.FILEFILTER_ALL, out filename ) )
      {
        ImportSprites( filename, true, true );
      }
    }



    public void Clear()
    {
      for ( int i = 0; i < m_SpriteProject.NumSprites; ++i )
      {
        m_SpriteProject.Sprites[i].Color = 0;
        m_SpriteProject.Sprites[i].Multicolor = false;
      }
      //DocumentInfo.DocumentFilename = "";
      m_SpriteProject.SpriteLayers.Clear();

      m_CurrentSprite = 0;
      m_CurrentLayer = new C64Studio.Formats.SpriteProject.Layer();
      m_CurrentLayer.Name = "Default";
      m_SpriteProject.SpriteLayers.Add( m_CurrentLayer );
      listLayerSprites.Items.Clear();
      listLayers.Items.Clear();

      foreach ( var layer in m_SpriteProject.SpriteLayers )
      {
        ArrangedItemEntry item = new ArrangedItemEntry( layer.Name );
        item.Tag = layer;
        listLayers.Items.Add( item );
      }
      CurrentSpriteModified();
      RedrawLayer();
    }



    private void CurrentSpriteModified()
    {
      // check if preview needs to be redrawn
      if ( m_CurrentLayer != null )
      {
        foreach ( var sprite in m_CurrentLayer.Sprites )
        {
          if ( sprite.Index == m_CurrentSprite )
          {
            RedrawLayer();
            break;
          }
        }
      }
    }



    public bool ImportSprites( string Filename, bool OnlyImportFromProject, bool AddUndo )
    {
      Clear();

      GR.Memory.ByteBuffer projectFile = GR.IO.File.ReadAllBytes( Filename );
      if ( projectFile == null )
      {
        return false;
      }

      GR.IO.MemoryReader memIn = projectFile.MemoryReader();

      if ( System.IO.Path.GetExtension( Filename ).ToUpper() == ".SPD" )
      {
        var spritePad = new SpritePadProject();

        if ( !spritePad.ReadFromBuffer( projectFile ) )
        {
          return false;
        }
        m_SpriteProject.BackgroundColor = spritePad.BackgroundColor;
        m_SpriteProject.MultiColor1     = spritePad.MultiColor1;
        m_SpriteProject.MultiColor2     = spritePad.MultiColor2;
        for ( int i = 0; i < spritePad.NumSprites; ++i )
        {
          if ( i < m_SpriteProject.Sprites.Count )
          {
            if ( AddUndo )
            {
              DocumentInfo.UndoManager.AddUndoTask( new Undo.UndoSpritesetSpriteChange( this, m_SpriteProject, i ), i == 0 );
            }

            spritePad.Sprites[i].Data.CopyTo( m_SpriteProject.Sprites[i].Data, 0, 63 );
            m_SpriteProject.Sprites[i].Color      = spritePad.Sprites[i].Color;
            m_SpriteProject.Sprites[i].Multicolor = spritePad.Sprites[i].Multicolor;
            RebuildSpriteImage( i );
          }
        }

        editSpriteFrom.Text = "0";
        editSpriteCount.Text = spritePad.NumSprites.ToString();

        comboBackground.SelectedIndex = m_SpriteProject.BackgroundColor;
        comboMulticolor1.SelectedIndex = m_SpriteProject.MultiColor1;
        comboMulticolor2.SelectedIndex = m_SpriteProject.MultiColor2;

        if ( ( m_SpriteProject.StartIndex != 0 )
        ||   ( m_SpriteProject.UsedSprites != 256 ) )
        {
          comboExportRange.SelectedIndex = 2;
        }

        panelSprites.Invalidate();
        pictureEditor.Invalidate();
        Modified = false;

        saveSpriteProjectToolStripMenuItem.Enabled = true;
        closeCharsetProjectToolStripMenuItem.Enabled = true;
        EnableFileWatcher();
        return true;
      }
      else if ( System.IO.Path.GetExtension( Filename ).ToUpper() != ".SPRITEPROJECT" )
      {
        //m_IsSpriteProject = false;
        int numSprites = (int)projectFile.Length / 64;
        for ( int i = 0; i < numSprites; ++i )
        {
          GR.Memory.ByteBuffer tempBuffer = new GR.Memory.ByteBuffer();

          memIn.ReadBlock( tempBuffer, 64 );
          if ( i < m_SpriteProject.Sprites.Count )
          {
            if ( AddUndo )
            {
              DocumentInfo.UndoManager.AddUndoTask( new Undo.UndoSpritesetSpriteChange( this, m_SpriteProject, i ), i == 0 );
            }

            tempBuffer.CopyTo( m_SpriteProject.Sprites[i].Data, 0, 63 );
            m_SpriteProject.Sprites[i].Color = ( tempBuffer.ByteAt( 63 ) & 0xf );
            m_SpriteProject.Sprites[i].Multicolor = ( ( tempBuffer.ByteAt( 63 ) & 0x80 ) != 0 );
            RebuildSpriteImage( i );
          }
        }

        editSpriteFrom.Text = "0";
        editSpriteCount.Text  = numSprites.ToString();

        panelSprites.Invalidate();
        pictureEditor.Invalidate();
        Modified = false;

        saveSpriteProjectToolStripMenuItem.Enabled = true;
        closeCharsetProjectToolStripMenuItem.Enabled = true;
        EnableFileWatcher();
        return true;
      }

      // sprite project
      if ( OnlyImportFromProject )
      {
        // only import sprite data
        Formats.SpriteProject   sprites = new C64Studio.Formats.SpriteProject();

        if ( !sprites.ReadFromBuffer( projectFile ) )
        {
          return false;
        }

        panelSprites.Items.Clear();
        comboSprite.Items.Clear();

        if ( AddUndo )
        {
          for ( int spriteIndex = 0; spriteIndex < m_SpriteProject.NumSprites; ++spriteIndex )
          {
            DocumentInfo.UndoManager.AddUndoTask( new Undo.UndoSpritesetSpriteChange( this, m_SpriteProject, spriteIndex ), spriteIndex == 0 );
          }
        }

        m_SpriteProject.NumSprites = sprites.NumSprites;
        m_SpriteProject.Sprites.Clear();
        for ( int i = 0; i < m_SpriteProject.NumSprites; ++i )
        {
          m_SpriteProject.Sprites.Add( sprites.Sprites[i].Clone() );

          RebuildSpriteImage( i );

          panelSprites.Items.Add( i.ToString(), m_SpriteProject.Sprites[i].Image );
          comboSprite.Items.Add( i );
        }
        comboSprite.SelectedIndex = 0;
        panelSprites.Invalidate();
        pictureEditor.Invalidate();
        Modified = false;
        return true;
      }

      if ( AddUndo )
      {
        DocumentInfo.UndoManager.AddUndoTask( new Undo.UndoSpritesetValuesChange( this, m_SpriteProject ), true );
        for ( int spriteIndex = 0; spriteIndex < m_SpriteProject.NumSprites; ++spriteIndex )
        {
          DocumentInfo.UndoManager.AddUndoTask( new Undo.UndoSpritesetSpriteChange( this, m_SpriteProject, spriteIndex ) );
        }
      }

      m_IsSpriteProject = true;
      m_CurrentLayer = null;
      m_SpriteProject.SpriteLayers.Clear();
      listLayerSprites.Items.Clear();
      listLayers.Items.Clear();

      if ( !m_SpriteProject.ReadFromBuffer( projectFile ) )
      {
        return false;
      }

      panelSprites.Items.Clear();
      comboSprite.Items.Clear();

      comboBackground.SelectedIndex = m_SpriteProject.BackgroundColor;
      comboMulticolor1.SelectedIndex = m_SpriteProject.MultiColor1;
      comboMulticolor2.SelectedIndex = m_SpriteProject.MultiColor2;

      editSpriteFrom.Text = m_SpriteProject.StartIndex.ToString();
      editSpriteCount.Text = m_SpriteProject.UsedSprites.ToString();
      if ( ( m_SpriteProject.StartIndex != 0 )
      ||   ( m_SpriteProject.UsedSprites != 256 ) )
      {
        comboExportRange.SelectedIndex = 2;
      }

      // re-add item (update tags)
      for ( int i = 0; i < m_SpriteProject.NumSprites; ++i )
      {
        panelSprites.Items.Add( i.ToString(), m_SpriteProject.Sprites[i].Image );
        comboSprite.Items.Add( i );
        RebuildSpriteImage( i );
      }
      pictureEditor.Image = m_SpriteProject.Sprites[m_CurrentSprite].Image;
      panelSprites.Invalidate();
      pictureEditor.Invalidate();

      if ( m_SpriteProject.SpriteLayers.Count == 0 )
      {
        m_CurrentLayer = new C64Studio.Formats.SpriteProject.Layer();
        m_CurrentLayer.Name = "Default";
        m_SpriteProject.SpriteLayers.Add( m_CurrentLayer );
      }
      else
      {
        m_CurrentLayer = m_SpriteProject.SpriteLayers[0];
      }
      comboLayerBGColor.SelectedIndex = m_CurrentLayer.BackgroundColor;

      AddAllLayers();
      if ( listLayers.Items.Count > 0 )
      {
        listLayers.SelectedIndices.Add( 0 );
      }
      //RedrawLayer();

      Modified = false;

      if ( DocumentInfo.Element == null )
      {
        DocumentInfo.DocumentFilename = Filename;
      }

      saveSpriteProjectToolStripMenuItem.Enabled = true;
      closeCharsetProjectToolStripMenuItem.Enabled = true;
      comboSprite.SelectedIndex = 0;
      EnableFileWatcher();
      return true;
    }



    private void AddAllLayers()
    {
      foreach ( var layer in m_SpriteProject.SpriteLayers )
      {
        ArrangedItemEntry item = new ArrangedItemEntry( layer.Name );
        item.Tag = layer;
        listLayers.Items.Add( item );
      }
      listLayers.UpdateUI();

      int   spriteIndex = 0;
      foreach ( var sprite in m_CurrentLayer.Sprites )
      {
        ArrangedItemEntry item = new ArrangedItemEntry();

        item.Text = sprite.Index.ToString() + ", " + sprite.X.ToString() + ", " + sprite.Y.ToString();
        item.Tag = sprite;
        listLayerSprites.Items.Add( item );

        ++spriteIndex;
      }
    }



    public override bool Load()
    {
      if ( string.IsNullOrEmpty( DocumentInfo.DocumentFilename ) )
      {
        return false;
      }
      try
      {
        ImportSprites( DocumentInfo.FullPath, false, false );
      }
      catch ( System.IO.IOException ex )
      {
        System.Windows.Forms.MessageBox.Show( "Could not load sprite project file " + DocumentInfo.FullPath + ".\r\n" + ex.Message, "Could not load file" );
        return false;
      }
      SetUnmodified();
      return true;
    }



    private bool SaveProject( bool SaveAs )
    {
      string    saveFilename = DocumentInfo.FullPath;

      if ( ( string.IsNullOrEmpty( DocumentInfo.DocumentFilename ) )
      ||   ( SaveAs ) )
      {
        System.Windows.Forms.SaveFileDialog saveDlg = new System.Windows.Forms.SaveFileDialog();

        saveDlg.Title = "Save Sprite Project as";
        saveDlg.Filter = "Sprite Projects|*.spriteproject|All Files|*.*";
        if ( DocumentInfo.Project != null )
        {
          saveDlg.InitialDirectory = DocumentInfo.Project.Settings.BasePath;
        }
        if ( saveDlg.ShowDialog() != System.Windows.Forms.DialogResult.OK )
        {
          return false;
        }
        saveFilename = saveDlg.FileName;
        if ( !SaveAs )
        {
          DocumentInfo.DocumentFilename = saveDlg.FileName;
          if ( DocumentInfo.Element != null )
          {
            DocumentInfo.DocumentFilename = GR.Path.RelativePathTo( saveDlg.FileName, false, System.IO.Path.GetFullPath( DocumentInfo.Project.Settings.BasePath ), true );
            DocumentInfo.Element.Name = System.IO.Path.GetFileNameWithoutExtension( DocumentInfo.DocumentFilename );
            DocumentInfo.Element.Node.Text = System.IO.Path.GetFileName( DocumentInfo.DocumentFilename );
            DocumentInfo.Element.Filename = DocumentInfo.DocumentFilename;
          }
          saveFilename = DocumentInfo.FullPath;
        }
      }

      GR.Memory.ByteBuffer dataToSave = SaveToBuffer();

      return SaveDocumentData( saveFilename, dataToSave, SaveAs );
    }



    private void closeCharsetProjectToolStripMenuItem_Click( object sender, EventArgs e )
    {
      if ( DocumentInfo.DocumentFilename == "" )
      {
        return;
      }
      if ( Modified )
      {
        DialogResult doSave = MessageBox.Show( "There are unsaved changes in your character set. Save now?", "Save changes?", MessageBoxButtons.YesNoCancel );
        if ( doSave == DialogResult.Cancel )
        {
          return;
        }
        if ( doSave == DialogResult.Yes )
        {
          SaveProject( false );
        }
      }
      Clear();
      DocumentInfo.DocumentFilename = "";
      Modified = false;
      panelSprites.Invalidate();
      pictureEditor.Invalidate();

      closeCharsetProjectToolStripMenuItem.Enabled = false;
      saveSpriteProjectToolStripMenuItem.Enabled = false;
    }



    private void saveCharsetProjectToolStripMenuItem_Click( object sender, EventArgs e )
    {
      SaveProject( false );
    }



    public override bool Save()
    {
      return SaveProject( false );
    }



    public override bool SaveAs()
    {
      return SaveProject( true );
    }



    public override GR.Memory.ByteBuffer SaveToBuffer()
    {
      GR.Memory.ByteBuffer projectFile = new GR.Memory.ByteBuffer();

      List<int> exportIndices = GetExportIndices();

      if ( m_IsSpriteProject )
      {
        projectFile = m_SpriteProject.SaveToBuffer();
      }
      else
      {
        for ( int i = 0; i < exportIndices.Count; ++i )
        {
          projectFile.Append( m_SpriteProject.Sprites[exportIndices[i]].Data );
          projectFile.AppendU8( (byte)m_SpriteProject.Sprites[exportIndices[i]].Color );
        }
      }
      return projectFile;
    }



    private List<int> GetExportIndices()
    {
      List<int>     exportIndices = new List<int>();

      switch ( comboExportRange.SelectedIndex )
      {
        case 0:
          // all
          for ( int i = 0; i < m_SpriteProject.NumSprites; ++i )
          {
            exportIndices.Add( i );
          }
          break;
        case 1:
          // selection
          exportIndices = panelSprites.SelectedIndices;
          break;
        case 2:
          // rage
          {
            int     startIndex = GR.Convert.ToI32( editSpriteFrom.Text );
            int     numSprites = GR.Convert.ToI32( editSpriteCount.Text );

            if ( startIndex < 0 )
            {
              startIndex = 0;
            }
            if ( startIndex >= m_SpriteProject.NumSprites )
            {
              startIndex = m_SpriteProject.NumSprites - 1;
            }
            if ( numSprites < 0 )
            {
              numSprites = 1;
            }
            if ( startIndex + numSprites > m_SpriteProject.NumSprites )
            {
              numSprites = m_SpriteProject.NumSprites - startIndex;
            }
            for ( int i = 0; i < numSprites; ++i )
            {
              exportIndices.Add( startIndex + i );
            }
          }
          break;
      }
      return exportIndices;
    }



    private void btnExportSprite_Click( object sender, EventArgs e )
    {
      var exportIndices = GetExportIndices();
      if ( exportIndices.Count == 0 )
      {
        return;
      }

      System.Windows.Forms.SaveFileDialog saveDlg = new System.Windows.Forms.SaveFileDialog();

      saveDlg.Title = "Export Sprites to";
      saveDlg.Filter = "Sprites|*.spr|All Files|*.*";
      saveDlg.FileName = m_SpriteProject.ExportFilename;
      if ( DocumentInfo.Project != null )
      {
        saveDlg.InitialDirectory = DocumentInfo.Project.Settings.BasePath;
      }
      if ( saveDlg.ShowDialog() != System.Windows.Forms.DialogResult.OK )
      {
        return;
      }
      if ( m_SpriteProject.ExportFilename != saveDlg.FileName )
      {
        m_SpriteProject.ExportFilename = saveDlg.FileName;
        Modified = true;
      }

      GR.Memory.ByteBuffer exportData = GatherExportData();
      GR.IO.File.WriteAllBytes( m_SpriteProject.ExportFilename, exportData );
    }



    private void btnExportSpriteToData_Click( object sender, EventArgs e )
    {
      int wrapByteCount = GR.Convert.ToI32( editWrapByteCount.Text );
      if ( wrapByteCount <= 0 )
      {
        wrapByteCount = 8;
      }
      string prefix = editPrefix.Text;

      GR.Memory.ByteBuffer exportData = GatherExportData();

      bool wrapData = checkExportToDataWrap.Checked;
      bool prefixRes = checkExportToDataIncludeRes.Checked;
      if ( !prefixRes )
      {
        prefix = "";
      }

      string line = Util.ToASMData( exportData, wrapData, wrapByteCount, prefix );
      editDataExport.Text = line;
    }



    private void checkExportToDataWrap_CheckedChanged( object sender, EventArgs e )
    {
      editWrapByteCount.Enabled = checkExportToDataWrap.Checked;
    }



    private void checkExportToDataIncludeRes_CheckedChanged( object sender, EventArgs e )
    {
      editPrefix.Enabled = checkExportToDataIncludeRes.Checked;
    }



    private void btnImportSprite_Click( object sender, EventArgs e )
    {
      string filename;

      if ( OpenFile( "Open sprite file", Types.Constants.FILEFILTER_SPRITE + Types.Constants.FILEFILTER_SPRITE_SPRITEPAD + Types.Constants.FILEFILTER_ALL, out filename ) )
      {
        ImportSprites( filename, true, true );
      }
    }



    private void btnPasteFromClipboard_Click( object sender, EventArgs e )
    {
      PasteFromClipboard();
      if ( m_ImportError.Length > 0 )
      {
        System.Windows.Forms.MessageBox.Show( m_ImportError, "Error while converting" );
      }
    }



    private void PasteFromClipboard()
    {
      m_ImportError = "";
      IDataObject dataObj = Clipboard.GetDataObject();
      if ( dataObj == null )
      {
        System.Windows.Forms.MessageBox.Show( "No image on clipboard" );
        return; 
      }
      if ( dataObj.GetDataPresent( "C64Studio.ImageList" ) )
      {
        System.IO.MemoryStream ms = (System.IO.MemoryStream)dataObj.GetData( "C64Studio.ImageList" );

        GR.Memory.ByteBuffer spriteData = new GR.Memory.ByteBuffer( (uint)ms.Length );

        ms.Read( spriteData.Data(), 0, (int)ms.Length );

        GR.IO.MemoryReader  memIn = spriteData.MemoryReader();

        int rangeCount = memIn.ReadInt32();
        bool columnBased = ( memIn.ReadInt32() > 0 ) ? true : false;

        int pastePos = panelSprites.SelectedIndex;
        if ( pastePos == -1 )
        {
          pastePos = 0;
        }

        for ( int i = 0; i < rangeCount; ++i )
        {
          int indexGap = memIn.ReadInt32();
          pastePos += indexGap;

          if ( pastePos >= m_SpriteProject.Sprites.Count )
          {
            break;
          }
          DocumentInfo.UndoManager.AddUndoTask( new Undo.UndoSpritesetSpriteChange( this, m_SpriteProject, pastePos ), i == 0 );

          var mode = (Types.CharsetMode)memIn.ReadInt32();
          m_SpriteProject.Sprites[pastePos].Multicolor = ( mode == Types.CharsetMode.MULTICOLOR );
          m_SpriteProject.Sprites[pastePos].Color = memIn.ReadInt32();

          uint width   = memIn.ReadUInt32();
          uint height  = memIn.ReadUInt32();

          uint dataLength = memIn.ReadUInt32();

          GR.Memory.ByteBuffer    tempBuffer = new GR.Memory.ByteBuffer();
          tempBuffer.Reserve( (int)dataLength );
          memIn.ReadBlock( tempBuffer, dataLength );

          m_SpriteProject.Sprites[pastePos].Data = new GR.Memory.ByteBuffer( 63 );

          if ( ( width == 24 )
          &&   ( height == 21 ) )
          {
            tempBuffer.CopyTo( m_SpriteProject.Sprites[pastePos].Data, 0, 63 );
          }
          else if ( ( width == 8 )
          &&        ( height == 8 ) )
          {
            for ( int j = 0; j < 8; ++j )
            {
              m_SpriteProject.Sprites[pastePos].Data.SetU8At( j * 3, tempBuffer.ByteAt( j ) );
            }
          }
          else
          {
            tempBuffer.CopyTo( m_SpriteProject.Sprites[pastePos].Data, 0, Math.Min( 8, (int)dataLength ) );
          }

          //memIn.ReadBlock( m_SpriteProject.Sprites[pastePos].Data, dataLength );
          int index = memIn.ReadInt32();

          RebuildSpriteImage( pastePos );
          panelSprites.InvalidateItemRect( pastePos );
          if ( pastePos == m_CurrentSprite )
          {
            CurrentSpriteModified();
            DoNotUpdateFromControls = true;
            comboSpriteColor.SelectedIndex = m_SpriteProject.Sprites[pastePos].Color;
            DoNotUpdateFromControls = false;
          }
        }
        pictureEditor.Invalidate();
        Modified = true;
        return;
      }
      else if ( dataObj.GetDataPresent( "C64Studio.Sprites" ) )
      {
        System.IO.MemoryStream ms = (System.IO.MemoryStream)dataObj.GetData( "C64Studio.Sprites" );

        GR.Memory.ByteBuffer spriteData = new GR.Memory.ByteBuffer( (uint)ms.Length );

        ms.Read( spriteData.Data(), 0, (int)ms.Length );

        // TODO
        return;
      }
      else if ( !Clipboard.ContainsImage() )
      {
        System.Windows.Forms.MessageBox.Show( "No image on clipboard" );
        return;
      }
      GR.Image.FastImage    imgClip = null;
      foreach ( string format in dataObj.GetFormats() )
      {
        if ( format == "DeviceIndependentBitmap" )
        {
          object dibData = dataObj.GetData( format );
          imgClip = GR.Image.FastImage.CreateImageFromHDIB( dibData );
          break;
        }
      }
      if ( imgClip == null )
      {
        System.Windows.Forms.MessageBox.Show( "No image on clipboard" );
        return;
      }
      GR.Image.FastImage mappedImage = null;

      Types.MulticolorSettings   mcSettings = new Types.MulticolorSettings();
      mcSettings.BackgroundColor  = m_SpriteProject.BackgroundColor;
      mcSettings.MultiColor1      = m_SpriteProject.MultiColor1;
      mcSettings.MultiColor2      = m_SpriteProject.MultiColor2;

      bool pasteAsBlock = false;
      if ( !Core.MainForm.ImportImage( "", imgClip, Types.GraphicType.SPRITES, mcSettings, out mappedImage, out mcSettings, out pasteAsBlock ) )
      {
        imgClip.Dispose();
        m_ImportError = "";
        return;
      }

      comboBackground.SelectedIndex = mcSettings.BackgroundColor;
      comboMulticolor1.SelectedIndex = mcSettings.MultiColor1;
      comboMulticolor2.SelectedIndex = mcSettings.MultiColor2;

      int spritesY = ( mappedImage.Height + 20 ) / 21;
      int spritesX = ( mappedImage.Width + 23 ) / 24;
      int spritesPerLine = panelSprites.ItemsPerLine;
      int currentTargetSprite = m_CurrentSprite;

      for ( int j = 0; j < spritesY; ++j )
      {
        for ( int i = 0; i < spritesX; ++i )
        {
          if ( pasteAsBlock )
          {
            int localX = ( m_CurrentSprite % spritesPerLine ) + i;
            int localY = m_CurrentSprite / spritesPerLine + j;
            if ( localX >= spritesPerLine )
            {
              continue;
            }
            if ( localY * spritesPerLine >= 256 )
            {
              break;
            }
            currentTargetSprite = localX + localY * spritesPerLine;
          }

          int copyWidth = mappedImage.Width - i * 24;
          if ( copyWidth > 24 )
          {
            copyWidth = 24;
          }
          int copyHeight = mappedImage.Height - j * 21;
          if ( copyHeight > 21 )
          {
            copyHeight = 21;
          }

          DocumentInfo.UndoManager.AddUndoTask( new Undo.UndoSpritesetSpriteChange( this, m_SpriteProject, currentTargetSprite ), ( i == 0 ) && ( j == 0 ) );

          GR.Image.FastImage imgSprite = mappedImage.GetImage( i * 24, j * 21, copyWidth, copyHeight ) as GR.Image.FastImage;
          ImportSprite( imgSprite, currentTargetSprite );
          imgSprite.Dispose();

          if ( currentTargetSprite == m_CurrentSprite )
          {
            CurrentSpriteModified();
            DoNotUpdateFromControls = true;
            comboSpriteColor.SelectedIndex = m_SpriteProject.Sprites[currentTargetSprite].Color;
            DoNotUpdateFromControls = false;
          }
          RebuildSpriteImage( currentTargetSprite );

          panelSprites.InvalidateItemRect( currentTargetSprite );

          if ( !pasteAsBlock )
          {
            ++currentTargetSprite;
          }
        }
      }
      mappedImage.Dispose();
      imgClip.Dispose();
      pictureEditor.Invalidate();
      Modified = true;
    }



    private void CopySpriteToClipboard()
    {
      // copy selected range/column (put custom data in clipboard)
      List<int>     selectedImages = panelSprites.SelectedIndices;
      if ( selectedImages.Count == 0 )
      {
        return;
      }

      GR.Memory.ByteBuffer      dataSelection = new GR.Memory.ByteBuffer();

      dataSelection.AppendI32( selectedImages.Count );
      dataSelection.AppendI32( panelSprites.IsSelectionColumnBased ? 1 : 0 );
      int prevIndex = selectedImages[0];
      foreach ( int index in selectedImages )
      {
        // delta in indices
        dataSelection.AppendI32( index - prevIndex );
        prevIndex = index;

        dataSelection.AppendI32( m_SpriteProject.Sprites[index].Multicolor ? 1 : 0 );
        dataSelection.AppendI32( m_SpriteProject.Sprites[index].Color );
        dataSelection.AppendU32( 24 );
        dataSelection.AppendU32( 21 );
        dataSelection.AppendU32( m_SpriteProject.Sprites[index].Data.Length );
        dataSelection.Append( m_SpriteProject.Sprites[index].Data );
        dataSelection.AppendI32( index );
      }

      DataObject    dataObj = new DataObject();

      dataObj.SetData( "C64Studio.ImageList", false, dataSelection.MemoryStream() );

      // add sprites as image
      var spriteImage = new GR.Image.MemoryImage( selectedImages.Count * 24, 21, System.Drawing.Imaging.PixelFormat.Format8bppIndexed );
      CustomRenderer.PaletteManager.ApplyPalette( spriteImage );
      int curX = 0;
      foreach ( int index in selectedImages )
      {
        var curSprite = m_SpriteProject.Sprites[index];
        spriteImage.Box( curX, 0, 24, 21, (uint)m_SpriteProject.BackgroundColor );
        if ( curSprite.Multicolor )
        {
          Displayer.SpriteDisplayer.DisplayMultiColorSprite( curSprite.Data,
                                                             m_SpriteProject.BackgroundColor,
                                                             m_SpriteProject.MultiColor1,
                                                             m_SpriteProject.MultiColor2,
                                                             curSprite.Color,
                                                             spriteImage, 
                                                             curX, 0 );
        }
        else
        {
          Displayer.SpriteDisplayer.DisplayHiResSprite( curSprite.Data,
                                                        m_SpriteProject.BackgroundColor,
                                                        curSprite.Color,
                                                        spriteImage, 
                                                        curX, 0 );
        }
        curX += 24;
      }
      GR.Memory.ByteBuffer      dibData2 = spriteImage.CreateHDIBAsBuffer();

      System.IO.MemoryStream    ms2 = dibData2.MemoryStream();
      dataObj.SetData( "DeviceIndependentBitmap", ms2 );

      Clipboard.SetDataObject( dataObj, true );
    }



    private void btnCopyToClipboard_Click( object sender, EventArgs e )
    {
      CopySpriteToClipboard();
    }



    private void btnExportSpriteToImage_Click( object sender, EventArgs e )
    {
      List<int> exportIndices = GetExportIndices();
      if ( exportIndices.Count == 0 )
      {
        return;
      }

      System.Windows.Forms.SaveFileDialog saveDlg = new System.Windows.Forms.SaveFileDialog();

      saveDlg.Title = "Export Sprites to Image";
      saveDlg.Filter = "PNG File|*.png";
      if ( saveDlg.ShowDialog() != System.Windows.Forms.DialogResult.OK )
      {
        return;
      }

      int     neededWidth = exportIndices.Count * 24;
      if ( neededWidth > 4 * 24 )
      {
        neededWidth = 4 * 24;
      }
      int     neededHeight = ( exportIndices.Count + 3 ) / 4 + 1;
      neededHeight *= 21;

      GR.Image.MemoryImage targetImg = new GR.Image.MemoryImage( neededWidth, neededHeight, m_SpriteProject.Sprites[0].Image.PixelFormat );

      CustomRenderer.PaletteManager.ApplyPalette( targetImg );
      for ( int i = 0; i < exportIndices.Count; ++i )
      {
        m_SpriteProject.Sprites[exportIndices[i]].Image.DrawTo( targetImg, ( i % 4 ) * 24, ( i / 4 ) * 21 );
      }
      System.Drawing.Bitmap bmpTarget = targetImg.GetAsBitmap();
      bmpTarget.Save( saveDlg.FileName, System.Drawing.Imaging.ImageFormat.Png );
    }



    private bool ImportSprite( GR.Image.FastImage Image, int SpriteIndex )
    {
      m_ImportError = "";
      /*
      if ( ( Image.Width != 24 )
      ||   ( Image.Height != 21 )
      ||   ( Image.PixelFormat != System.Drawing.Imaging.PixelFormat.Format8bppIndexed ) )*/
      if ( Image.PixelFormat != System.Drawing.Imaging.PixelFormat.Format8bppIndexed )
      {
        // invalid format
        m_ImportError = "Invalid image format, must be 8 bit index";
        return false;
      }

      // Match image data
      GR.Memory.ByteBuffer Buffer = new GR.Memory.ByteBuffer( m_SpriteProject.Sprites[SpriteIndex].Data );

      int   ChosenSpriteColor = -1;

      bool  isMultiColor = false;

      unsafe
      {
        // determine single/multi color
        bool[]  usedColor = new bool[16];
        int     numColors = 0;
        bool    hasSinglePixel = false;
        bool    usedBackgroundColor = false;

        for ( int y = 0; y < Image.Height; ++y )
        {
          for ( int x = 0; x < Image.Width; ++x )
          {
            int     colorIndex = (int)Image.GetPixelData( x, y );
            if ( colorIndex >= 16 )
            {
              m_ImportError = "Encountered color index >= 16 at " + x + "," + y;
              return false;
            }
            if ( ( x % 2 ) == 0 )
            {
              if ( colorIndex != (int)Image.GetPixelData( x + 1, y ) )
              {
                // not a double pixel, must be single color then
                hasSinglePixel = true;
              }
            }

            if ( !usedColor[colorIndex] )
            {
              if ( colorIndex == m_SpriteProject.BackgroundColor )
              {
                usedBackgroundColor = true;
              }
              usedColor[colorIndex] = true;
              numColors++;
            }
          }
        }
        if ( ( hasSinglePixel )
        &&   ( numColors > 2 ) )
        {
          m_ImportError = "Has a single pixel, but more than two colors";
          return false;
        }
        if ( ( hasSinglePixel )
        &&   ( numColors == 2 )
        &&   ( !usedBackgroundColor ) )
        {
          m_ImportError = "Looks like single color, but doesn't use the set background color";
          return false;
        }
        if ( ( !hasSinglePixel )
        &&   ( numColors > 4 ) )
        {
          m_ImportError = "Uses more than 4 colors";
          return false;
        }
        if ( ( !hasSinglePixel )
        &&   ( numColors == 4 )
        &&   ( !usedBackgroundColor ) )
        {
          m_ImportError = "Uses 4 colors, but doesn't use the set background color";
          return false;
        }
        if ( ( hasSinglePixel )
        ||   ( ( numColors == 2 )
        &&     ( usedBackgroundColor ) ) )
        {
          // eligible for single color
          int usedFreeColor = -1;
          for ( int i = 0; i < 16; ++i )
          {
            if ( usedColor[i] )
            {
              if ( i != m_SpriteProject.BackgroundColor )
              {
                if ( usedFreeColor != -1 )
                {
                  m_ImportError = "Uses more than one free color";
                  return false;
                }
                usedFreeColor = i;
              }
            }
          }

          for ( int y = 0; y < Image.Height; ++y )
          {
            for ( int x = 0; x < Image.Width; ++x )
            {
              int ColorIndex = (int)Image.GetPixelData( x, y );

              int BitPattern = 0;

              if ( ColorIndex != m_SpriteProject.BackgroundColor )
              {
                BitPattern = 1;
              }

              // noch nicht verwendete Farbe
              if ( BitPattern == 1 )
              {
                ChosenSpriteColor = ColorIndex;
              }
              byte byteMask = (byte)( 255 - ( 1 << ( ( 7 - ( x % 8 ) ) ) ) );
              Buffer.SetU8At( y * 3 + x / 8, (byte)( ( Buffer.ByteAt( y * 3 + x / 8 ) & byteMask ) | ( BitPattern << ( ( 7 - ( x % 8 ) ) ) ) ) );
            }
          }
        }
        else
        {
          // multi color
          isMultiColor = true;
          int     usedMultiColors = 0;
          int     usedFreeColor = -1;
          for ( int i = 0; i < 16; ++i )
          {
            if ( usedColor[i] )
            {
              if ( ( i == m_SpriteProject.MultiColor1 )
              ||   ( i == m_SpriteProject.MultiColor2 )
              ||   ( i == m_SpriteProject.BackgroundColor ) )
              {
                ++usedMultiColors;
              }
              else
              {
                usedFreeColor = i;
              }
            }
          }
          if ( numColors - usedMultiColors > 1 )
          {
            // only one free color allowed
            m_ImportError = "Uses more than one free color";
            return false;
          }
          for ( int y = 0; y < Image.Height; ++y )
          {
            for ( int x = 0; x < Image.Width / 2; ++x )
            {
              int ColorIndex = (int)Image.GetPixelData( 2 * x, y );

              byte BitPattern = 0;

              if ( ColorIndex == m_SpriteProject.BackgroundColor )
              {
                BitPattern = 0x00;
              }
              else if ( ColorIndex == m_SpriteProject.MultiColor1 )
              {
                BitPattern = 0x01;
              }
              else if ( ColorIndex == m_SpriteProject.MultiColor2 )
              {
                BitPattern = 0x03;
              }
              else
              {
                // noch nicht verwendete Farbe
                ChosenSpriteColor = usedFreeColor;
                BitPattern = 0x02;
              }
              byte byteMask = (byte)( 255 - ( 3 << ( ( 3 - ( x % 4 ) ) * 2 ) ) );
              Buffer.SetU8At( y * 3 + x / 4, (byte)( ( Buffer.ByteAt( y * 3 + x / 4 ) & byteMask ) | ( BitPattern << ( ( 3 - ( x % 4 ) ) * 2 ) ) ) );
            }
          }
        }
      }
      for ( int i = 0; i < 63; ++i )
      {
        m_SpriteProject.Sprites[SpriteIndex].Data.SetU8At( i, Buffer.ByteAt( i ) );
      }
      m_SpriteProject.Sprites[SpriteIndex].Color = ChosenSpriteColor;
      m_SpriteProject.Sprites[SpriteIndex].Multicolor = isMultiColor;
      RebuildSpriteImage( SpriteIndex );
      return true;
    }



    private void btnImportFromImage_Click( object sender, EventArgs e )
    {
      string filename;

      if ( !OpenFile( "Import Sprites from Image", C64Studio.Types.Constants.FILEFILTER_IMAGE_FILES, out filename ) )
      {
        return;
      }

      GR.Image.FastImage spriteImage;

      Types.MulticolorSettings   mcSettings = new Types.MulticolorSettings();
      mcSettings.BackgroundColor  = m_SpriteProject.BackgroundColor;
      mcSettings.MultiColor1      = m_SpriteProject.MultiColor1;
      mcSettings.MultiColor2      = m_SpriteProject.MultiColor2;

      bool pasteAsBlock = false;
      if ( !Core.MainForm.ImportImage(filename, null, C64Studio.Types.GraphicType.SPRITES, mcSettings, out spriteImage, out mcSettings, out pasteAsBlock ) )
      {
        return;
      }

      comboBackground.SelectedIndex = mcSettings.BackgroundColor;
      comboMulticolor1.SelectedIndex = mcSettings.MultiColor1;
      comboMulticolor2.SelectedIndex = mcSettings.MultiColor2;

      int   currentSpriteIndex = 0;
      int   curX = 0;
      int   curY = 0;
      while ( curY + 21 <= spriteImage.Height )
      {
        DocumentInfo.UndoManager.AddUndoTask( new Undo.UndoSpritesetSpriteChange( this, m_SpriteProject, currentSpriteIndex ), currentSpriteIndex == 0 );

        ImportSprite( spriteImage.GetImage( curX, curY, 24, 21 ) as GR.Image.FastImage, currentSpriteIndex );

        ++currentSpriteIndex;
        curX += 24;
        if ( curX >= spriteImage.Width )
        {
          curX = 0;
          curY += 21;
        }
      }
      panelSprites.Invalidate();
      pictureEditor.Invalidate();
      Modified = true;
    }



    private void btnShiftLeft_Click( object sender, EventArgs e )
    {
      ShiftLeft();
    }



    private void ShiftLeft()
    {
      var selectedSprites = panelSprites.SelectedIndices;

      bool firstEntry = true;
      foreach ( var spriteIndex in selectedSprites )
      {
        DocumentInfo.UndoManager.AddUndoTask( new Undo.UndoSpritesetSpriteChange( this, m_SpriteProject, spriteIndex ), firstEntry );
        firstEntry = false;
        var sprite = m_SpriteProject.Sprites[spriteIndex];

        for ( int y = 0; y < 21; ++y )
        {
          byte    byte1 = sprite.Data.ByteAt( y * 3 );
          byte    byte2 = sprite.Data.ByteAt( y * 3 + 1 );
          byte    byte3 = sprite.Data.ByteAt( y * 3 + 2 );
          if ( sprite.Multicolor )
          {
            byte1 <<= 2;
            byte1 |= (byte)( byte2 >> 6 );
            byte2 <<= 2;
            byte2 |= (byte)( byte3 >> 6 );
            byte3 <<= 2;
            byte3 |= (byte)( sprite.Data.ByteAt( y * 3 ) >> 6 );
          }
          else
          {
            byte1 <<= 1;
            byte1 |= (byte)( byte2 >> 7 );
            byte2 <<= 1;
            byte2 |= (byte)( byte3 >> 7 );
            byte3 <<= 1;
            byte3 |= (byte)( sprite.Data.ByteAt( y * 3 ) >> 7 );
          }
          sprite.Data.SetU8At( y * 3, byte1 );
          sprite.Data.SetU8At( y * 3 + 1, byte2 );
          sprite.Data.SetU8At( y * 3 + 2, byte3 );
        }
        RebuildSpriteImage( spriteIndex );
        panelSprites.InvalidateItemRect( spriteIndex );
      }
      pictureEditor.Invalidate();
      Modified = true;
    }



    private void btnShiftRight_Click( object sender, EventArgs e )
    {
      ShiftRight();
    }



    private void ShiftRight()
    {
      var selectedSprites = panelSprites.SelectedIndices;

      bool firstEntry = true;
      foreach ( var spriteIndex in selectedSprites )
      {
        DocumentInfo.UndoManager.AddUndoTask( new Undo.UndoSpritesetSpriteChange( this, m_SpriteProject, spriteIndex ), firstEntry );
        firstEntry = false;
        var sprite = m_SpriteProject.Sprites[spriteIndex];

        for ( int y = 0; y < 21; ++y )
        {
          byte    byte1 = sprite.Data.ByteAt( y * 3 );
          byte    byte2 = sprite.Data.ByteAt( y * 3 + 1 );
          byte    byte3 = sprite.Data.ByteAt( y * 3 + 2 );
          if ( sprite.Multicolor )
          {
            byte1 >>= 2;
            byte1 |= (byte)( byte3 << 6 );
            byte3 >>= 2;
            byte3 |= (byte)( byte2 << 6 );
            byte2 >>= 2;
            byte2 |= (byte)( sprite.Data.ByteAt( y * 3 ) << 6 );
          }
          else
          {
            byte1 >>= 1;
            byte1 |= (byte)( byte3 << 7 );
            byte3 >>= 1;
            byte3 |= (byte)( byte2 << 7 );
            byte2 >>= 1;
            byte2 |= (byte)( sprite.Data.ByteAt( y * 3 ) << 7 );
          }
          sprite.Data.SetU8At( y * 3, byte1 );
          sprite.Data.SetU8At( y * 3 + 1, byte2 );
          sprite.Data.SetU8At( y * 3 + 2, byte3 );
        }
        RebuildSpriteImage( spriteIndex );
        panelSprites.InvalidateItemRect( spriteIndex );
      }
      pictureEditor.Invalidate();
      Modified = true;
    }



    private void btnShiftUp_Click( object sender, EventArgs e )
    {
      ShiftUp();
    }



    private void ShiftUp()
    {
      var selectedSprites = panelSprites.SelectedIndices;

      bool firstEntry = true;
      foreach ( var spriteIndex in selectedSprites )
      {
        DocumentInfo.UndoManager.AddUndoTask( new Undo.UndoSpritesetSpriteChange( this, m_SpriteProject, spriteIndex ), firstEntry );
        firstEntry = false;
        var sprite = m_SpriteProject.Sprites[spriteIndex];

        byte    byte1 = sprite.Data.ByteAt( 0 );
        byte    byte2 = sprite.Data.ByteAt( 1 );
        byte    byte3 = sprite.Data.ByteAt( 2 );
        for ( int y = 0; y < 20; ++y )
        {
          sprite.Data.SetU8At( y * 3,     sprite.Data.ByteAt( ( y + 1 ) * 3 ) );
          sprite.Data.SetU8At( y * 3 + 1, sprite.Data.ByteAt( ( y + 1 ) * 3 + 1 ) );
          sprite.Data.SetU8At( y * 3 + 2, sprite.Data.ByteAt( ( y + 1 ) * 3 + 2 ) );
        }
        sprite.Data.SetU8At( 20 * 3, byte1 );
        sprite.Data.SetU8At( 20 * 3 + 1, byte2 );
        sprite.Data.SetU8At( 20 * 3 + 2, byte3 );

        RebuildSpriteImage( spriteIndex );
        panelSprites.InvalidateItemRect( spriteIndex );
      }
      pictureEditor.Invalidate();
      Modified = true;
    }



    private void btnShiftDown_Click( object sender, EventArgs e )
    {
      ShiftDown();
    }



    private void ShiftDown()
    {
      var selectedSprites = panelSprites.SelectedIndices;

      bool firstEntry = true;
      foreach ( var spriteIndex in selectedSprites )
      {
        DocumentInfo.UndoManager.AddUndoTask( new Undo.UndoSpritesetSpriteChange( this, m_SpriteProject, spriteIndex ), firstEntry );
        firstEntry = false;
        var sprite = m_SpriteProject.Sprites[spriteIndex];

        byte    byte1 = sprite.Data.ByteAt( 20 * 3 );
        byte    byte2 = sprite.Data.ByteAt( 20 * 3 + 1 );
        byte    byte3 = sprite.Data.ByteAt( 20 * 3 + 2 );
        for ( int y = 0; y < 20; ++y )
        {
          sprite.Data.SetU8At( ( 20 - y ) * 3,     sprite.Data.ByteAt( ( 20 - y - 1 ) * 3 ) );
          sprite.Data.SetU8At( ( 20 - y ) * 3 + 1, sprite.Data.ByteAt( ( 20 - y - 1 ) * 3 + 1 ) );
          sprite.Data.SetU8At( ( 20 - y ) * 3 + 2, sprite.Data.ByteAt( ( 20 - y - 1 ) * 3 + 2 ) );
        }
        sprite.Data.SetU8At( 0, byte1 );
        sprite.Data.SetU8At( 1, byte2 );
        sprite.Data.SetU8At( 2, byte3 );

        RebuildSpriteImage( spriteIndex );
        panelSprites.InvalidateItemRect( spriteIndex );
      }
      pictureEditor.Invalidate();
      Modified = true;
    }



    private void btnMirrorX_Click( object sender, EventArgs e )
    {
      MirrorX();
    }



    private void btnMirrorY_Click( object sender, EventArgs e )
    {
      MirrorY();
    }



    void pictureEditor_PostPaint( GR.Image.FastImage TargetBuffer )
    {
      if ( m_SpriteProject.ShowGrid )
      {
        if ( m_SpriteProject.Sprites[m_CurrentSprite].Multicolor )
        {
          for ( int i = 0; i < 12; ++i )
          {
            for ( int j = 0; j < TargetBuffer.Height; ++j )
            {
              TargetBuffer.SetPixel( ( i * pictureEditor.ClientRectangle.Width / 12 ), j, 0xffffffff );
            }
          }
          for ( int i = 0; i < 21; ++i )
          {
            for ( int j = 0; j < TargetBuffer.Width; ++j )
            {
              TargetBuffer.SetPixel( j, ( i * pictureEditor.ClientRectangle.Height / 21 ), 0xffffffff );
            }
          }
        }
        else
        {
          for ( int i = 0; i < 24; ++i )
          {
            for ( int j = 0; j < TargetBuffer.Height; ++j )
            {
              TargetBuffer.SetPixel( i * pictureEditor.ClientRectangle.Width / 24, j, 0xffffffff );
            }
          }
          for ( int i = 0; i < 21; ++i )
          {
            for ( int j = 0; j < TargetBuffer.Width; ++j )
            {
              TargetBuffer.SetPixel( j, ( i * pictureEditor.ClientRectangle.Height / 21 ), 0xffffffff );
            }
          }
        }
      }
    }



    private void checkShowGrid_CheckedChanged( object sender, EventArgs e )
    {
      m_SpriteProject.ShowGrid = checkShowGrid.Checked;
      pictureEditor.Invalidate();
    }



    private void btnInvert_Click( object sender, EventArgs e )
    {
      Invert();
    }



    private void Invert()
    {
      var selectedSprites = panelSprites.SelectedIndices;

      bool firstEntry = true;
      foreach ( var spriteIndex in selectedSprites )
      {
        DocumentInfo.UndoManager.AddUndoTask( new Undo.UndoSpritesetSpriteChange( this, m_SpriteProject, spriteIndex ), firstEntry );
        firstEntry = false;
        var sprite = m_SpriteProject.Sprites[spriteIndex];

        for ( int i = 0; i < 63; ++i )
        {
          byte value = (byte)( ~sprite.Data.ByteAt( i ) );
          sprite.Data.SetU8At( i, value );
        }
        RebuildSpriteImage( spriteIndex );
        panelSprites.InvalidateItemRect( spriteIndex );
      }
      pictureEditor.Invalidate();
      Modified = true;
    }



    private void btnRotateLeft_Click( object sender, EventArgs e )
    {
      RotateLeft();
    }



    private void RotateLeft()
    {
      var selectedSprites = panelSprites.SelectedIndices;

      bool firstEntry = true;
      foreach ( var spriteIndex in selectedSprites )
      {
        DocumentInfo.UndoManager.AddUndoTask( new Undo.UndoSpritesetSpriteChange( this, m_SpriteProject, spriteIndex ), firstEntry );
        firstEntry = false;
        var sprite = m_SpriteProject.Sprites[spriteIndex];

        GR.Memory.ByteBuffer    resultData = new GR.Memory.ByteBuffer( 63 );

        if ( !sprite.Multicolor )
        {
          for ( int i = 0; i < 21; ++i )
          {
            for ( int j = 0; j < 21; ++j )
            {
              int sourceX = 1 + i;
              int sourceY = j;
              int targetX = j;
              int targetY = 20 - i;
              if ( ( sprite.Data.ByteAt( sourceX / 8 + sourceY * 3 ) & ( 1 << ( 7 - ( sourceX % 8 ) ) ) ) != 0 )
              {
                resultData.SetU8At( targetY * 3 + targetX / 8, (byte)( resultData.ByteAt( targetY * 3 + targetX / 8 ) | ( 1 << ( 7 - targetX % 8 ) ) ) );
              }
            }
          }
        }
        else
        {
          for ( int i = 0; i < 24; i += 2 )
          {
            for ( int j = 0; j < 21; ++j )
            {
              int sourceX = 20 - j;
              int sourceY = i;

              if ( ( sourceX < 0 )
              || ( sourceX >= 24 )
              || ( sourceY < 0 )
              || ( sourceY >= 21 ) )
              {
                continue;
              }
              int maskOffset = 6 - ( ( sourceX % 8 ) / 2 ) * 2;
              byte sourceColor = (byte)( ( sprite.Data.ByteAt( sourceX / 8 + sourceY * 3 ) & ( 3 << maskOffset ) ) >> maskOffset );

              maskOffset = 6 - ( ( i % 8 ) / 2 ) * 2;
              resultData.SetU8At( j * 3 + i / 8, (byte)( resultData.ByteAt( j * 3 + i / 8 ) | ( sourceColor << maskOffset ) ) );
            }
          }
        }
        sprite.Data = resultData;
        RebuildSpriteImage( spriteIndex );
        panelSprites.InvalidateItemRect( spriteIndex );
      }
      pictureEditor.Invalidate();
      Modified = true;
    }



    private void btnRotateRight_Click( object sender, EventArgs e )
    {
      RotateRight();
    }



    private void RotateRight()
    {
      var selectedSprites = panelSprites.SelectedIndices;

      bool firstEntry = true;
      foreach ( var spriteIndex in selectedSprites )
      {
        DocumentInfo.UndoManager.AddUndoTask( new Undo.UndoSpritesetSpriteChange( this, m_SpriteProject, spriteIndex ), firstEntry );
        firstEntry = false;
        var sprite = m_SpriteProject.Sprites[spriteIndex];

        GR.Memory.ByteBuffer resultData = new GR.Memory.ByteBuffer( 63 );

        if ( !sprite.Multicolor )
        {
          for ( int i = 0; i < 21; ++i )
          {
            for ( int j = 0; j < 21; ++j )
            {
              int sourceX = 1 + i;
              int sourceY = j;
              int targetX = 20 - j;
              int targetY = i;
              if ( ( sprite.Data.ByteAt( sourceX / 8 + sourceY * 3 ) & ( 1 << ( 7 - ( sourceX % 8 ) ) ) ) != 0 )
              {
                resultData.SetU8At( targetY * 3 + targetX / 8, (byte)( resultData.ByteAt( targetY * 3 + targetX / 8 ) | ( 1 << ( 7 - targetX % 8 ) ) ) );
              }
            }
          }
        }
        else
        {
          for ( int i = 0; i < 24; i += 2 )
          {
            for ( int j = 0; j < 21; ++j )
            {
              int sourceX = j;
              int sourceY = 20 - i;

              if ( ( sourceX < 0 )
              || ( sourceX >= 24 )
              || ( sourceY < 0 )
              || ( sourceY >= 21 ) )
              {
                continue;
              }
              int maskOffset = 6 - ( ( sourceX % 8 ) / 2 ) * 2;
              byte sourceColor = (byte)( ( sprite.Data.ByteAt( sourceX / 8 + sourceY * 3 ) & ( 3 << maskOffset ) ) >> maskOffset );

              maskOffset = 6 - ( ( i % 8 ) / 2 ) * 2;
              resultData.SetU8At( j * 3 + i / 8, (byte)( resultData.ByteAt( j * 3 + i / 8 ) | ( sourceColor << maskOffset ) ) );
            }
          }
        }
        sprite.Data = resultData;
        RebuildSpriteImage( spriteIndex );
        panelSprites.InvalidateItemRect( spriteIndex );
      }
      pictureEditor.Invalidate();
      Modified = true;
    }



    private void comboSprite_DrawItem( object sender, DrawItemEventArgs e )
    {
      e.DrawBackground();
      if ( e.Index == -1 )
      {
        return;
      }
      System.Drawing.RectangleF textRect = new System.Drawing.RectangleF( e.Bounds.Left, e.Bounds.Top + 10, e.Bounds.Width, e.Bounds.Height );
      System.Drawing.Brush textBrush = new System.Drawing.SolidBrush( e.ForeColor );
      e.Graphics.DrawString( e.Index.ToString(), e.Font, textBrush, textRect );

      GR.Image.FastImage    fastImage = new GR.Image.FastImage( 24, 21, System.Drawing.Imaging.PixelFormat.Format8bppIndexed );
      GR.Image.MemoryImage  memImage = new GR.Image.MemoryImage( 24, 21, System.Drawing.Imaging.PixelFormat.Format8bppIndexed );

      CustomRenderer.PaletteManager.ApplyPalette( fastImage );
      CustomRenderer.PaletteManager.ApplyPalette( memImage );

      DrawSpriteImage( memImage, m_SpriteProject.Sprites[e.Index].Data, m_SpriteProject.BackgroundColor, comboLayerColor.SelectedIndex, m_SpriteProject.Sprites[e.Index].Multicolor, m_SpriteProject.MultiColor1, m_SpriteProject.MultiColor2 );
      fastImage.DrawImage( memImage, 0, 0 );
      System.Drawing.Rectangle drawRect = new System.Drawing.Rectangle( e.Bounds.Location, e.Bounds.Size );
      drawRect.X += 20;
      drawRect.Width = 48;
      drawRect.Height = 42;
      fastImage.DrawToHDC( e.Graphics.GetHdc(), drawRect );
      e.Graphics.ReleaseHdc();
      fastImage.Dispose();
    }



    private void comboLayerColor_SelectedIndexChanged( object sender, EventArgs e )
    {
      comboSprite.Invalidate();

      if ( listLayerSprites.SelectedIndices.Count > 0 )
      {
        Formats.SpriteProject.LayerSprite sprite = (Formats.SpriteProject.LayerSprite)listLayerSprites.SelectedItems[0].Tag;

        if ( sprite.Color != comboLayerColor.SelectedIndex )
        {
          sprite.Color = comboLayerColor.SelectedIndex;
          RedrawLayer();
          SetModified();
        }
      }
    }



    private void btnAdd_Click( object sender, EventArgs e )
    {
    }



    private void RedrawLayer()
    {
      if ( m_CurrentLayer != null )
      {
        layerPreview.DisplayPage.Box( 0, 0, layerPreview.Width, layerPreview.Height, (uint)m_CurrentLayer.BackgroundColor );
        foreach ( Formats.SpriteProject.LayerSprite sprite in m_CurrentLayer.Sprites )
        {
          DrawSpriteImage( layerPreview.DisplayPage, 
                           sprite.X, sprite.Y, 
                           m_SpriteProject.Sprites[sprite.Index].Data, 
                           sprite.Color, 
                           m_SpriteProject.Sprites[sprite.Index].Multicolor, 
                           m_SpriteProject.MultiColor1, 
                           m_SpriteProject.MultiColor2,
                           sprite.ExpandX, sprite.ExpandY );
        }
      }
      else
      {
        layerPreview.DisplayPage.Box( 0, 0, layerPreview.Width, layerPreview.Height, 0 );
      }
      layerPreview.Invalidate();
    }



    private void btnDelete_Click( object sender, EventArgs e )
    {
      if ( listLayerSprites.SelectedIndices.Count == 0 )
      {
        return;
      }
      Formats.SpriteProject.LayerSprite sprite = (Formats.SpriteProject.LayerSprite)listLayerSprites.SelectedItems[0].Tag;

      m_CurrentLayer.Sprites.Remove( sprite );
      listLayerSprites.Items.Remove( listLayerSprites.SelectedItems[0] );
      RedrawLayer();
      SetModified();
    }



    private void btnUp_Click( object sender, EventArgs e )
    {
      if ( ( listLayerSprites.SelectedIndices.Count > 0 )
      &&   ( listLayerSprites.SelectedIndices[0] > 0 ) )
      {
        int insertIndex = listLayerSprites.SelectedIndices[0] - 1;

        Formats.SpriteProject.LayerSprite sprite1 = (Formats.SpriteProject.LayerSprite)listLayerSprites.SelectedItems[0].Tag;
        Formats.SpriteProject.LayerSprite sprite2 = (Formats.SpriteProject.LayerSprite)listLayerSprites.Items[insertIndex].Tag;
        
        m_CurrentLayer.Sprites.Remove( sprite1 );
        m_CurrentLayer.Sprites.Insert( insertIndex, sprite1 );

        ArrangedItemEntry itemToSwap = listLayerSprites.SelectedItems[0];
        listLayerSprites.Items.Remove( itemToSwap );
        listLayerSprites.Items.Insert( insertIndex, itemToSwap );
        itemToSwap.Selected = true;
        RedrawLayer();
        SetModified();
      }
    }



    private void btnDown_Click( object sender, EventArgs e )
    {
      if ( ( listLayerSprites.SelectedIndices.Count > 0 )
      &&   ( listLayerSprites.SelectedIndices[0] + 1 < listLayerSprites.Items.Count ) )
      {
        int insertIndex = listLayerSprites.SelectedIndices[0] + 1;

        Formats.SpriteProject.LayerSprite sprite1 = (Formats.SpriteProject.LayerSprite)listLayerSprites.SelectedItems[0].Tag;
        Formats.SpriteProject.LayerSprite sprite2 = (Formats.SpriteProject.LayerSprite)listLayerSprites.Items[insertIndex].Tag;

        m_CurrentLayer.Sprites.Remove( sprite1 );
        m_CurrentLayer.Sprites.Insert( insertIndex, sprite1 );

        ArrangedItemEntry itemToSwap = listLayerSprites.SelectedItems[0];
        listLayerSprites.Items.Remove( itemToSwap );
        listLayerSprites.Items.Insert( insertIndex, itemToSwap );
        itemToSwap.Selected = true;
        RedrawLayer();
        SetModified();
      }

    }



    private void comboSprite_SelectedIndexChanged( object sender, EventArgs e )
    {
      if ( listLayerSprites.SelectedIndices.Count > 0 )
      {
        Formats.SpriteProject.LayerSprite sprite = (Formats.SpriteProject.LayerSprite)listLayerSprites.SelectedItems[0].Tag;

        if ( sprite.Index != comboSprite.SelectedIndex )
        {
          sprite.Index = comboSprite.SelectedIndex;
          listLayerSprites.SelectedItems[0].Text = sprite.Index.ToString() + ", " + sprite.X.ToString() + ", " + sprite.Y.ToString();
          RedrawLayer();
          SetModified();
        }
      }
    }



    private void editLayerX_TextChanged( object sender, EventArgs e )
    {
      if ( listLayerSprites.SelectedIndices.Count > 0 )
      {
        Formats.SpriteProject.LayerSprite sprite = (Formats.SpriteProject.LayerSprite)listLayerSprites.SelectedItems[0].Tag;

        int newPos = GR.Convert.ToI32( editLayerX.Text );
        if ( sprite.X != newPos )
        {
          sprite.X = newPos;
          listLayerSprites.SelectedItems[0].Text = sprite.Index.ToString() + ", " + sprite.X.ToString() + ", " + sprite.Y.ToString();
          RedrawLayer();
          SetModified();
        }
      }
    }



    private void editLayerY_TextChanged( object sender, EventArgs e )
    {
      if ( listLayerSprites.SelectedIndices.Count > 0 )
      {
        Formats.SpriteProject.LayerSprite sprite = (Formats.SpriteProject.LayerSprite)listLayerSprites.SelectedItems[0].Tag;

        int newPos = GR.Convert.ToI32( editLayerY.Text );
        if ( sprite.Y != newPos )
        {
          sprite.Y = newPos;
          listLayerSprites.SelectedItems[0].Text = sprite.Index.ToString() + ", " + sprite.X.ToString() + ", " + sprite.Y.ToString();
          RedrawLayer();
          SetModified();
        }
      }
    }



    private void comboLayerBGColor_SelectedIndexChanged( object sender, EventArgs e )
    {
      if ( m_CurrentLayer == null )
      {
        return;
      }

      if ( comboLayerBGColor.SelectedIndex != m_CurrentLayer.BackgroundColor )
      {
        m_CurrentLayer.BackgroundColor = comboLayerBGColor.SelectedIndex;
        RedrawLayer();
        SetModified();
      }
    }



    private void btnDeleteSprite_Click( object sender, EventArgs e )
    {
      if ( panelSprites.SelectedIndex == -1 )
      {
        return;
      }

      for ( int i = 0; i < m_SpriteProject.Sprites.Count; ++i )
      {
        DocumentInfo.UndoManager.AddUndoTask( new Undo.UndoSpritesetSpriteChange( this, m_SpriteProject, i ), i == 0 );
      }

      List<int>     selectedSprites = panelSprites.SelectedIndices;

      int     firstSelectedIndex = 0;
      if ( selectedSprites.Count > 0 )
      {
        firstSelectedIndex = selectedSprites[0];
      }

      selectedSprites.Reverse();

      foreach ( var index in selectedSprites )
      {
        int indexToRemove = index;

        m_SpriteProject.Sprites.RemoveAt( indexToRemove );
        panelSprites.Items.RemoveAt( indexToRemove );

        // add empty sprite in back
        m_SpriteProject.Sprites.Add( new C64Studio.Formats.SpriteProject.SpriteData() );
        panelSprites.Items.Add( ( m_SpriteProject.Sprites.Count - 1 ).ToString(), m_SpriteProject.Sprites[m_SpriteProject.Sprites.Count - 1].Image );
      }
      if ( firstSelectedIndex < panelSprites.Items.Count )
      {
        panelSprites.SelectedIndex = firstSelectedIndex;
      }
      else
      {
        panelSprites.SelectedIndex = 0;
      }
      panelCharacters_SelectedIndexChanged( sender, e );

      SetModified();
    }



    private void editDataExport_KeyPress( object sender, KeyPressEventArgs e )
    {
      if ( ( System.Windows.Forms.Control.ModifierKeys == Keys.Control )
      &&   ( e.KeyChar == 1 ) )
      {
        editDataExport.SelectAll();
        e.Handled = true;
      }
    }



    private ArrangedItemEntry listLayerSprites_AddingItem( object sender )
    {
      Formats.SpriteProject.LayerSprite sprite = new Formats.SpriteProject.LayerSprite();
      sprite.X        = GR.Convert.ToI32( editLayerX.Text );
      sprite.Y        = GR.Convert.ToI32( editLayerY.Text );
      sprite.Index    = comboSprite.SelectedIndex;
      sprite.Color    = comboLayerColor.SelectedIndex;
      sprite.ExpandX  = checkExpandX.Checked;
      sprite.ExpandY  = checkExpandY.Checked;

      m_CurrentLayer.Sprites.Add( sprite );

      ArrangedItemEntry item = new ArrangedItemEntry();
      item.Text = sprite.Index.ToString() + ", " + sprite.X.ToString() + ", " + sprite.Y.ToString();
      item.Tag = sprite;

      return item;
    }



    private void listLayerSprites_ItemMoved( object sender, ArrangedItemEntry Item, ArrangedItemEntry OtherItem )
    {
      m_CurrentLayer.Sprites.Clear();
      foreach ( ArrangedItemEntry item in listLayerSprites.Items )
      {
        var sprite = (Formats.SpriteProject.LayerSprite)item.Tag;

        m_CurrentLayer.Sprites.Add( sprite );
      }
      SetModified();
      RedrawLayer();
    }



    private void listLayerSprites_ItemRemoved( object sender, ArrangedItemEntry Item )
    {
      m_CurrentLayer.Sprites.Clear();
      foreach ( ArrangedItemEntry item in listLayerSprites.Items )
      {
        var sprite = (Formats.SpriteProject.LayerSprite)item.Tag;

        m_CurrentLayer.Sprites.Add( sprite );
      }
      SetModified();
      RedrawLayer();
    }



    private void listLayerSprites_SelectedIndexChanged( object sender, ArrangedItemEntry Item )
    {
      if ( Item != null )
      {
        Formats.SpriteProject.LayerSprite sprite = (Formats.SpriteProject.LayerSprite)Item.Tag;
        editLayerX.Text               = sprite.X.ToString();
        editLayerY.Text               = sprite.Y.ToString();
        comboLayerColor.SelectedIndex = sprite.Color;
        comboSprite.SelectedIndex     = sprite.Index;
        checkExpandX.Checked          = sprite.ExpandX;
        checkExpandY.Checked          = sprite.ExpandY;
      }
    }



    private void listLayers_ItemAdded( object sender, ArrangedItemEntry Item )
    {
      m_CurrentLayer = (Formats.SpriteProject.Layer)Item.Tag;
      SetModified();
      RedrawLayer();
    }



    private void listLayerSprites_ItemAdded( object sender, ArrangedItemEntry Item )
    {
      var sprite = (Formats.SpriteProject.LayerSprite)Item.Tag;
      SetModified();
      RedrawLayer();
    }



    private ArrangedItemEntry listLayers_AddingItem( object sender )
    {
      DocumentInfo.UndoManager.AddUndoTask( new Undo.UndoSpritesetAddLayer( this, m_SpriteProject, m_SpriteProject.SpriteLayers.Count ) );

      Formats.SpriteProject.Layer   layer = new C64Studio.Formats.SpriteProject.Layer();

      layer.Name = editLayerName.Text;

      var item = new ArrangedItemEntry( editLayerName.Text );
      item.Tag = layer;

      m_SpriteProject.SpriteLayers.Add( layer );

      return item;
    }



    private void listLayers_SelectedIndexChanged( object sender, ArrangedItemEntry Item )
    {
      listLayerSprites.Items.Clear();
      if ( Item == null )
      {
        m_CurrentLayer = null;
        editLayerName.Text = "";
        editLayerDelay.Text = "";
        RedrawLayer();
        return;
      }
      m_CurrentLayer = (Formats.SpriteProject.Layer)Item.Tag;
      if ( editLayerName.Text != m_CurrentLayer.Name )
      {
        editLayerName.Text = m_CurrentLayer.Name;
      }
      string  delayText = m_CurrentLayer.DelayMS.ToString();
      if ( editLayerDelay.Text != delayText )
      {
        editLayerDelay.Text = delayText;
      }

      comboLayerBGColor.SelectedIndex = m_CurrentLayer.BackgroundColor;

      int   spriteIndex = 0;
      foreach ( var sprite in m_CurrentLayer.Sprites )
      {
        var item = new ArrangedItemEntry();

        item.Text = sprite.Index.ToString() + ", " + sprite.X.ToString() + ", " + sprite.Y.ToString();
        item.Tag = sprite;
        listLayerSprites.Items.Add( item );

        ++spriteIndex;
      }
      RedrawLayer();
    }



    private void listLayers_ItemRemoved( object sender, ArrangedItemEntry Item )
    {
      m_SpriteProject.SpriteLayers.Clear();
      foreach ( ArrangedItemEntry item in listLayers.Items )
      {
        var layer = (Formats.SpriteProject.Layer)item.Tag;

        m_SpriteProject.SpriteLayers.Add( layer );
      }
      SetModified();
      if ( m_CurrentLayer == (Formats.SpriteProject.Layer)Item.Tag )
      {
        m_CurrentLayer = null;
        RedrawLayer();
      }
    }



    private void editLayerName_TextChanged( object sender, EventArgs e )
    {
      if ( m_CurrentLayer != null )
      {
        if ( m_CurrentLayer.Name != editLayerName.Text )
        {
          m_CurrentLayer.Name = editLayerName.Text;
          listLayers.SelectedItems[0].Text = m_CurrentLayer.Name;
        }
      }
    }



    private void listLayers_ItemMoved( object sender, ArrangedItemEntry Item, ArrangedItemEntry OtherItem )
    {
      m_SpriteProject.SpriteLayers.Clear();
      foreach ( ArrangedItemEntry item in listLayers.Items )
      {
        var layer = (Formats.SpriteProject.Layer)item.Tag;

        m_SpriteProject.SpriteLayers.Add( layer );
      }
      SetModified();
    }



    private void comboExportRange_SelectedIndexChanged( object sender, EventArgs e )
    {
      labelCharactersFrom.Enabled = ( comboExportRange.SelectedIndex == 2 );
      editSpriteFrom.Enabled = ( comboExportRange.SelectedIndex == 2 );
      labelCharactersTo.Enabled = ( comboExportRange.SelectedIndex == 2 );
      editSpriteCount.Enabled = ( comboExportRange.SelectedIndex == 2 );
    }



    private void editSpriteFrom_TextChanged( object sender, EventArgs e )
    {
      int   newStart = GR.Convert.ToI32( editSpriteFrom.Text );

      if ( m_SpriteProject.StartIndex != newStart )
      {
        m_SpriteProject.StartIndex = newStart;
        SetModified();
      }
    }



    private void editSpriteCount_TextChanged( object sender, EventArgs e )
    {
      uint   newCount = GR.Convert.ToU32( editSpriteCount.Text );

      if ( m_SpriteProject.UsedSprites != newCount )
      {
        m_SpriteProject.UsedSprites = newCount;
        SetModified();
      }
    }



    public void SpriteChanged( int SpriteIndex )
    {
      RebuildSpriteImage( SpriteIndex );
      pictureEditor.Invalidate();
      panelSprites.InvalidateItemRect( SpriteIndex );
      if ( m_CurrentSprite == SpriteIndex )
      {
        // TODO - can only do those once a list of undo steps has been completely finished undoing
        DoNotUpdateFromControls = true;
        if ( m_SpriteProject.Sprites[SpriteIndex].Multicolor != checkMulticolor.Checked )
        {
          checkMulticolor.Checked = m_SpriteProject.Sprites[SpriteIndex].Multicolor;
        }
        if ( m_SpriteProject.Sprites[SpriteIndex].Color != comboSpriteColor.SelectedIndex )
        {
          comboSpriteColor.SelectedIndex = m_SpriteProject.Sprites[SpriteIndex].Color;
        }
        DoNotUpdateFromControls = false;
      }
      CurrentSpriteModified();
    }



    public void ColorsChanged()
    {
      comboMulticolor1.SelectedIndex = m_SpriteProject.MultiColor1;
      comboMulticolor2.SelectedIndex = m_SpriteProject.MultiColor2;
      comboBackground.SelectedIndex = m_SpriteProject.BackgroundColor;

      for ( int i = 0; i < m_SpriteProject.NumSprites; ++i )
      {
        RebuildSpriteImage( i );
      }
      pictureEditor.Invalidate();
      panelSprites.Invalidate();
    }



    public void LayersChanged()
    {
      int   currentLayer = m_SpriteProject.SpriteLayers.IndexOf( m_CurrentLayer );

      listLayerSprites.Items.Clear();
      listLayers.Items.Clear();

      AddAllLayers();
      RedrawLayer();
    }



    private bool listLayerSprites_MovingItem( object sender, ArrangedItemEntry Item1, ArrangedItemEntry Item2 )
    {
      return true;
    }



    private bool listLayers_MovingItem( object sender, ArrangedItemEntry Item1, ArrangedItemEntry Item2 )
    {
      DocumentInfo.UndoManager.AddUndoTask( new Undo.UndoSpritesetExchangeLayer( this, m_SpriteProject, Item1.Index, Item2.Index ) );

      return true;
    }



    private void checkExpandX_CheckedChanged( object sender, EventArgs e )
    {
      if ( listLayerSprites.SelectedIndices.Count > 0 )
      {
        Formats.SpriteProject.LayerSprite sprite = (Formats.SpriteProject.LayerSprite)listLayerSprites.SelectedItems[0].Tag;

        if ( sprite.ExpandX != checkExpandX.Checked )
        {
          sprite.ExpandX = checkExpandX.Checked;
          Modified = true;
          RedrawLayer();
          SetModified();
        }
      }
    }



    private void checkExpandY_CheckedChanged( object sender, EventArgs e )
    {
      if ( listLayerSprites.SelectedIndices.Count > 0 )
      {
        Formats.SpriteProject.LayerSprite sprite = (Formats.SpriteProject.LayerSprite)listLayerSprites.SelectedItems[0].Tag;

        if ( sprite.ExpandY != checkExpandY.Checked )
        {
          sprite.ExpandY = checkExpandY.Checked;
          Modified = true;
          RedrawLayer();
          SetModified();
        }
      }
    }



    private void editLayerX_KeyPress( object sender, KeyPressEventArgs e )
    {
      if ( ( char.IsDigit( e.KeyChar ) )
      ||   ( (Keys)e.KeyChar == Keys.Back )
      ||   ( ( e.KeyChar == '-' )
      &&     ( editLayerX.SelectionStart == 0 ) ) )
      {
        // ok
      }
      else
      {
        e.Handled = true;
      }
    }



    private void editLayerY_KeyPress( object sender, KeyPressEventArgs e )
    {
      if ( ( char.IsDigit( e.KeyChar ) )
      ||   ( (Keys)e.KeyChar == Keys.Back )
      ||   ( ( e.KeyChar == '-' )
      &&     ( editLayerY.SelectionStart == 0 ) ) )
      {
        // ok
      }
      else
      {
        e.Handled = true;
      }

    }



    private void btnImportFromHex_Click( object sender, EventArgs e )
    {
      string    binaryText = editDataImport.Text.Replace( " ", "" ).Replace( "\r", "" ).Replace( "\n", "" );

      GR.Memory.ByteBuffer    data = new GR.Memory.ByteBuffer( binaryText );

      int numSprites = ( (int)data.Length + 63 ) / 64;
      for ( int i = 0; i < numSprites; ++i )
      {
        var tempBuffer = new GR.Memory.ByteBuffer( 64 );

        int     numBytesToCopy = 64;
        if ( i * 64 + numBytesToCopy > (int)data.Length )
        {
          numBytesToCopy = (int)data.Length - i * 64;
        }
        data.CopyTo( tempBuffer, i * 64, numBytesToCopy );


        if ( i < m_SpriteProject.Sprites.Count )
        {
          DocumentInfo.UndoManager.AddUndoTask( new Undo.UndoSpritesetSpriteChange( this, m_SpriteProject, i ), i == 0 );

          tempBuffer.CopyTo( m_SpriteProject.Sprites[i].Data, 0, 63 );
          m_SpriteProject.Sprites[i].Color = ( tempBuffer.ByteAt( 63 ) & 0xf );
          RebuildSpriteImage( i );
        }
      }

      panelSprites.Invalidate();
      pictureEditor.Invalidate();
      Modified = true;
    }



    public void ExchangeMultiColors()
    {
      int   temp = m_SpriteProject.MultiColor1;
      m_SpriteProject.MultiColor1 = m_SpriteProject.MultiColor2;
      m_SpriteProject.MultiColor2 = temp;

      List<int>     allIndices = new List<int>();
      for ( int i = 0; i < 256; ++i )
      {
        allIndices.Add( i );
      }
      ExchangeMultiColors( allIndices, false );

      pictureEditor.Invalidate();

      comboMulticolor1.SelectedIndex = m_SpriteProject.MultiColor1;
      comboMulticolor2.SelectedIndex = m_SpriteProject.MultiColor2;

      Modified = true;
    }



    private void ExchangeMultiColors( List<int> SelectedSprites, bool AddUndo )
    {
      bool    firstEntry = true;
      foreach ( int spriteIndex in SelectedSprites )
      {
        var sprite = m_SpriteProject.Sprites[spriteIndex];
        if ( sprite.Multicolor )
        {
          if ( AddUndo )
          {
            DocumentInfo.UndoManager.AddUndoTask( new Undo.UndoSpritesetSpriteChange( this, m_SpriteProject, spriteIndex ), firstEntry );
          }
          firstEntry = false;
          ReplaceSpriteColors( sprite, 1, 3 );
          RebuildSpriteImage( spriteIndex );
          panelSprites.InvalidateItemRect( spriteIndex );
        }
      }
    }



    private void ReplaceSpriteColors( SpriteProject.SpriteData Sprite, int PixelPattern1, int PixelPattern2 )
    {
      if ( Sprite == null )
      {
        Debug.Log( "ReplaceSpriteColors invalid sprite passed" );
        return;
      }
      if ( ( PixelPattern1 < 0 )
      ||   ( PixelPattern1 > 3 )
      ||   ( PixelPattern2 < 0 )
      ||   ( PixelPattern2 > 3 ) )
      {
        Debug.Log( "ReplaceSpriteColors: Invalid pixel patterns passed " + PixelPattern1 + "," + PixelPattern2 );
        return;
      }
      for ( int y = 0; y < 21; ++y )
      {
        for ( int x = 0; x < 3; ++x )
        {
          int     bytePos = y * 3 + x;
          for ( int b = 0; b < 4; ++b )
          {
            int pixelValue = ( Sprite.Data.ByteAt( bytePos ) & ( 3 << ( ( 3 - b ) * 2 ) ) ) >> ( ( 3 - b ) * 2 );

            if ( pixelValue == PixelPattern1 )
            {
              byte  newValue = (byte)( Sprite.Data.ByteAt( bytePos ) & ~( 3 << ( ( 3 - b ) * 2 ) ) );

              newValue |= (byte)( PixelPattern2 << ( ( 3 - b ) * 2 ) );

              Sprite.Data.SetU8At( bytePos, newValue );
            }
            else if ( pixelValue == PixelPattern2 )
            {
              byte  newValue = (byte)( Sprite.Data.ByteAt( bytePos ) & ~( 3 << ( ( 3 - b ) * 2 ) ) );

              newValue |= (byte)( PixelPattern1 << ( ( 3 - b ) * 2 ) );

              Sprite.Data.SetU8At( bytePos, newValue );
            }
          }
        }
      }
    }



    public void ExchangeMultiColor1WithBackground()
    {
      int   temp = m_SpriteProject.BackgroundColor;
      m_SpriteProject.BackgroundColor = m_SpriteProject.MultiColor1;
      m_SpriteProject.MultiColor1 = temp;

      List<int>     allIndices = new List<int>();
      for ( int i = 0; i < 256; ++i )
      {
        allIndices.Add( i );
      }
      ExchangeMultiColor1WithBackground( allIndices, false );

      pictureEditor.Invalidate();
      comboMulticolor1.SelectedIndex = m_SpriteProject.MultiColor1;
      comboBackground.SelectedIndex = m_SpriteProject.BackgroundColor;

    }



    public void ExchangeMultiColor1WithBackground( List<int> SelectedSprites, bool AddUndo )
    {
      bool    firstEntry = true;
      foreach ( int spriteIndex in SelectedSprites )
      {
        var sprite = m_SpriteProject.Sprites[spriteIndex];
        if ( sprite.Multicolor )
        {
          if ( AddUndo )
          {
            DocumentInfo.UndoManager.AddUndoTask( new Undo.UndoSpritesetSpriteChange( this, m_SpriteProject, spriteIndex ), firstEntry );
          }
          firstEntry = false;
          ReplaceSpriteColors( sprite, 0, 1 );
          RebuildSpriteImage( spriteIndex );
          panelSprites.Invalidate();
        }
      }
      Modified = true;
    }



    public void ExchangeMultiColor2WithBackground( List<int> SelectedSprites, bool AddUndo )
    {
      bool    firstEntry = true;
      foreach ( int spriteIndex in SelectedSprites )
      {
        var sprite = m_SpriteProject.Sprites[spriteIndex];
        if ( sprite.Multicolor )
        {
          if ( AddUndo )
          {
            DocumentInfo.UndoManager.AddUndoTask( new Undo.UndoSpritesetSpriteChange( this, m_SpriteProject, spriteIndex ), firstEntry );
          }
          firstEntry = false;
          ReplaceSpriteColors( sprite, 3, 0 );
          RebuildSpriteImage( spriteIndex );
          panelSprites.Invalidate();
        }
      }
      Modified = true;
    }



    void ExchangeBGColorWithSpriteColor( List<int> SpritesToModify, bool AddUndo )
    {
      bool firstEntry = true;
      foreach ( var spriteIndex in SpritesToModify )
      {
        var sprite = m_SpriteProject.Sprites[spriteIndex];

        if ( !sprite.Multicolor )
        {
          continue;
        }
        if ( AddUndo )
        {
          DocumentInfo.UndoManager.AddUndoTask( new Undo.UndoSpritesetSpriteChange( this, m_SpriteProject, spriteIndex ), firstEntry );
        }
        firstEntry = false;
        ReplaceSpriteColors( sprite, 0, 2 );
        RebuildSpriteImage( spriteIndex );
        panelSprites.InvalidateItemRect( spriteIndex );
      }
      pictureEditor.Invalidate();
      Modified = true;
    }



    private void exchangeMultiColor1WithMultiColor2ToolStripMenuItem_Click( object sender, EventArgs e )
    {
      DocumentInfo.UndoManager.AddUndoTask( new Undo.UndoSpritesetExchangeMultiColors( this, C64Studio.Undo.UndoSpritesetExchangeMultiColors.ExchangeMode.MULTICOLOR_1_WITH_MULTICOLOR_2 ) );

      ExchangeMultiColors();
    }

    
    
    private void exchangeMultiColor1WithBGColorToolStripMenuItem_Click( object sender, EventArgs e )
    {
      DocumentInfo.UndoManager.AddUndoTask( new Undo.UndoSpritesetExchangeMultiColors( this, C64Studio.Undo.UndoSpritesetExchangeMultiColors.ExchangeMode.MULTICOLOR_1_WITH_BACKGROUND ) );

      ExchangeMultiColor1WithBackground();
    }



    private void exchangeMultiColor2WithBGColorToolStripMenuItem_Click( object sender, EventArgs e )
    {
      DocumentInfo.UndoManager.AddUndoTask( new Undo.UndoSpritesetExchangeMultiColors( this, C64Studio.Undo.UndoSpritesetExchangeMultiColors.ExchangeMode.MULTICOLOR_2_WITH_BACKGROUND ) );

      ExchangeMultiColor2WithBackground();
    }



    public void ExchangeMultiColor2WithBackground()
    {
      int   temp = m_SpriteProject.BackgroundColor;
      m_SpriteProject.BackgroundColor = m_SpriteProject.MultiColor2;
      m_SpriteProject.MultiColor2 = temp;

      List<int>     allIndices = new List<int>();
      for ( int i = 0; i < 256; ++i )
      {
        allIndices.Add( i );
      }
      ExchangeMultiColor2WithBackground( allIndices, false );

      pictureEditor.Invalidate();
      comboMulticolor2.SelectedIndex = m_SpriteProject.MultiColor2;
      comboBackground.SelectedIndex = m_SpriteProject.BackgroundColor;
    }



    private void panelSprites_Resize( object sender, EventArgs e )
    {
      panelSprites.SetDisplaySize( panelSprites.ClientSize.Width / 2, panelSprites.ClientSize.Height / 2 );
    }



    void ExchangeMultiColor1WithSpriteColor( List<int> SpritesToModify, bool AddUndo )
    {
      bool firstEntry = true;
      foreach ( var spriteIndex in SpritesToModify )
      {
        var sprite = m_SpriteProject.Sprites[spriteIndex];

        if ( !sprite.Multicolor )
        {
          continue;
        }
        if ( AddUndo )
        {
          DocumentInfo.UndoManager.AddUndoTask( new Undo.UndoSpritesetSpriteChange( this, m_SpriteProject, spriteIndex ), firstEntry );
        }
        firstEntry = false;
        ReplaceSpriteColors( sprite, 1, 2 );
        RebuildSpriteImage( spriteIndex );
        panelSprites.InvalidateItemRect( spriteIndex );
      }
      pictureEditor.Invalidate();
      Modified = true;
    }



    void ExchangeMultiColor2WithSpriteColor( List<int> SpritesToModify, bool AddUndo )
    {
      bool firstEntry = true;
      foreach ( var spriteIndex in SpritesToModify )
      {
        var sprite = m_SpriteProject.Sprites[spriteIndex];

        if ( !sprite.Multicolor )
        {
          continue;
        }
        if ( AddUndo )
        {
          DocumentInfo.UndoManager.AddUndoTask( new Undo.UndoSpritesetSpriteChange( this, m_SpriteProject, spriteIndex ), firstEntry );
        }
        firstEntry = false;
        ReplaceSpriteColors( sprite, 2, 3 );
        RebuildSpriteImage( spriteIndex );
        panelSprites.InvalidateItemRect( spriteIndex );
      }
      pictureEditor.Invalidate();
      Modified = true;
    }



    private void exchangeMulticolor1WithSpriteColorToolStripMenuItem1_Click( object sender, EventArgs e )
    {
      ExchangeMultiColor1WithSpriteColor( panelSprites.SelectedIndices, true );
    }



    private void exchangeMulticolor2WithSpriteColorToolStripMenuItem1_Click( object sender, EventArgs e )
    {
      ExchangeMultiColor2WithSpriteColor( panelSprites.SelectedIndices, true );
    }



    private void btnExportToBASICData_Click( object sender, EventArgs e )
    {
      int startLine = GR.Convert.ToI32( editExportBASICLineNo.Text );
      if ( ( startLine < 0 )
      ||   ( startLine > 63999 ) )
      {
        startLine = 10;
      }
      int lineOffset = GR.Convert.ToI32( editExportBASICLineOffset.Text );
      if ( ( lineOffset < 0 )
      ||   ( lineOffset > 63999 ) )
      {
        startLine = 10;
      }

      GR.Memory.ByteBuffer exportData = GatherExportData();
      if ( checkExportToDataWrap.Checked )
      {
        editDataExport.Text = Util.ToBASICData( exportData, startLine, lineOffset, GR.Convert.ToI32( editWrapByteCount.Text ) );
      }
      else
      {
        editDataExport.Text = Util.ToBASICData( exportData, startLine, lineOffset, 80 );
      }
    }



    private void exchangeBGColorWithSpriteColorToolStripMenuItem_Click( object sender, EventArgs e )
    {
      ExchangeBGColorWithSpriteColor( panelSprites.SelectedIndices, true );
    }



    private void exchangeMulticolor1WithBGColorSelectedSpritesToolStripMenuItem_Click( object sender, EventArgs e )
    {
      ExchangeMultiColor1WithBackground( panelSprites.SelectedIndices, true );
    }



    private void exchangeMulticolor2WithBGColorSelectedSpritesToolStripMenuItem_Click( object sender, EventArgs e )
    {
      ExchangeMultiColor2WithBackground( panelSprites.SelectedIndices, true );
    }



    private void exchangeMulticolor1WithMulticolor2ToolStripMenuItem1_Click( object sender, EventArgs e )
    {
      ExchangeMultiColors( panelSprites.SelectedIndices, true );
    }



    private void btnImportFromASM_Click( object sender, EventArgs e )
    {
      Parser.ASMFileParser asmParser = new C64Studio.Parser.ASMFileParser();

      Parser.CompileConfig config = new Parser.CompileConfig();
      config.TargetType = Types.CompileTargetType.PLAIN;
      config.OutputFile = "temp.bin";
      config.Assembler = Types.AssemblerType.C64_STUDIO;

      string    temp = "* = $0801\n" + editDataImport.Text;
      if ( ( asmParser.Parse( temp, null, config, null ) )
      &&   ( asmParser.Assemble( config ) ) )
      {
        GR.Memory.ByteBuffer spriteData = asmParser.AssembledOutput.Assembly;

        ImportFromData( spriteData );
      }
    }



    private void ImportFromData( ByteBuffer SpriteData )
    {
      if ( SpriteData == null )
      {
        return;
      }
      int   spriteSizeGuess = 63;
      if ( ( SpriteData.Length % 64 ) == 0 )
      {
        spriteSizeGuess = 64;
      }
      int numSprites = (int)SpriteData.Length / spriteSizeGuess;
      for ( int i = 0; i < numSprites; ++i )
      {
        DocumentInfo.UndoManager.AddUndoTask( new Undo.UndoSpritesetSpriteChange( this, m_SpriteProject, i ), i == 0 );

        SpriteData.CopyTo( m_SpriteProject.Sprites[i].Data, i * spriteSizeGuess, 63 );
        if ( spriteSizeGuess == 64 )
        {
          m_SpriteProject.Sprites[i].Color = ( SpriteData.ByteAt( i * 64 + 63 ) & 0xf );
        }
        else
        {
          m_SpriteProject.Sprites[i].Color = 1;
        }
        RebuildSpriteImage( i );
      }

      editSpriteFrom.Text = "0";
      editSpriteCount.Text = numSprites.ToString();

      panelSprites.Invalidate();
      pictureEditor.Invalidate();
      Modified = false;

      saveSpriteProjectToolStripMenuItem.Enabled = true;
      closeCharsetProjectToolStripMenuItem.Enabled = true;
    }



    private void btnImportFromBASIC_Click( object sender, EventArgs e )
    {
      ImportFromData( Util.FromBASIC( editDataImport.Text ) );
      Modified = true;
    }



    public override bool ApplyFunction( Function Function )
    {
      if ( !pictureEditor.Focused )
      {
        return false;
      }
      switch ( Function )
      {
        case Function.GRAPHIC_ELEMENT_MIRROR_H:
          MirrorX();
          return true;
        case Function.GRAPHIC_ELEMENT_MIRROR_V:
          MirrorY();
          return true;
        case Function.GRAPHIC_ELEMENT_SHIFT_D:
          ShiftDown();
          return true;
        case Function.GRAPHIC_ELEMENT_SHIFT_U:
          ShiftUp();
          return true;
        case Function.GRAPHIC_ELEMENT_SHIFT_L:
          ShiftLeft();
          return true;
        case Function.GRAPHIC_ELEMENT_SHIFT_R:
          ShiftRight();
          return true;
        case Function.GRAPHIC_ELEMENT_ROTATE_L:
          RotateLeft();
          return true;
        case Function.GRAPHIC_ELEMENT_ROTATE_R:
          RotateRight();
          return true;
        case Function.GRAPHIC_ELEMENT_INVERT:
          Invert();
          return true;
        case Function.GRAPHIC_ELEMENT_PREVIOUS:
          Previous();
          return true;
        case Function.GRAPHIC_ELEMENT_NEXT:
          Next();
          return true;
        case Function.GRAPHIC_ELEMENT_CUSTOM_COLOR:
          CustomColor();
          return true;
        case Function.GRAPHIC_ELEMENT_MULTI_COLOR_1:
          MultiColor1();
          return true;
        case Function.GRAPHIC_ELEMENT_MULTI_COLOR_2:
          MultiColor2();
          return true;
      }
      return base.ApplyFunction( Function );
    }



    private void MultiColor2()
    {
      comboMulticolor2.SelectedIndex = ( comboMulticolor2.SelectedIndex + 1 ) % 16;
    }



    private void MultiColor1()
    {
      comboMulticolor1.SelectedIndex = ( comboMulticolor1.SelectedIndex + 1 ) % 16;
    }



    private void CustomColor()
    {
      comboSpriteColor.SelectedIndex = ( comboSpriteColor.SelectedIndex + 1 ) % 16;
    }



    private void Next()
    {
      panelSprites.SelectedIndex = ( panelSprites.SelectedIndex + 1 ) % 256;
    }



    private void Previous()
    {
      panelSprites.SelectedIndex = ( panelSprites.SelectedIndex + 256 - 1 ) % 256;
    }



    private void btnExportToBASICHexData_Click( object sender, EventArgs e )
    {
      GR.Memory.ByteBuffer exportData = GatherExportData();

      int startLine = GR.Convert.ToI32( editExportBASICLineNo.Text );

      if ( ( startLine < 0 )
      ||   ( startLine > 63999 ) )
      {
        startLine = 10;
      }
      int lineOffset = GR.Convert.ToI32( editExportBASICLineOffset.Text );
      if ( ( lineOffset < 0 )
      ||   ( lineOffset > 63999 ) )
      {
        startLine = 10;
      }
      if ( checkExportToDataWrap.Checked )
      {
        editDataExport.Text = Util.ToBASICHexData( exportData, startLine, lineOffset, GR.Convert.ToI32( editWrapByteCount.Text ) );
      }
      else
      {
        editDataExport.Text = Util.ToBASICHexData( exportData, startLine, lineOffset );
      }
    }



    private ByteBuffer GatherExportData()
    {
      GR.Memory.ByteBuffer exportData = new GR.Memory.ByteBuffer();

      var exportIndices = GetExportIndices();
      for ( int i = 0; i < exportIndices.Count; ++i )
      {
        exportData.Append( m_SpriteProject.Sprites[exportIndices[i]].Data );

        byte color = (byte)m_SpriteProject.Sprites[exportIndices[i]].Color;
        if ( m_SpriteProject.Sprites[exportIndices[i]].Multicolor )
        {
          color |= 0x80;
        }
        exportData.AppendU8( color );
      }
      return exportData;
    }



    private void btnImportFromBASICHex_Click( object sender, EventArgs e )
    {
      ImportFromData( Util.FromBASICHex( editDataImport.Text ) );
      Modified = true;
    }



    private void editDataImport_KeyPress( object sender, KeyPressEventArgs e )
    {
      if ( ( System.Windows.Forms.Control.ModifierKeys == Keys.Control )
      &&   ( e.KeyChar == 1 ) )
      {
        editDataImport.SelectAll();
        e.Handled = true;
      }
    }



    private void btnClear_Click( object sender, EventArgs e )
    {
      editDataImport.Clear();
    }



    private ArrangedItemEntry listLayers_CloningItem( object sender, ArrangedItemEntry Item )
    {
      DocumentInfo.UndoManager.AddUndoTask( new Undo.UndoSpritesetAddLayer( this, m_SpriteProject, m_SpriteProject.SpriteLayers.Count ) );

      var layer = new SpriteProject.Layer();
      var origLayer = (SpriteProject.Layer)Item.Tag;

      layer.Name = origLayer.Name;
      layer.BackgroundColor = origLayer.BackgroundColor;
      foreach ( var sprite in origLayer.Sprites )
      {
        layer.Sprites.Add( new SpriteProject.LayerSprite() { Color = sprite.Color, ExpandX = sprite.ExpandX, ExpandY = sprite.ExpandY, Index = sprite.Index, X = sprite.X, Y = sprite.Y } );
      }
      var item = new ArrangedItemEntry( layer.Name );
      item.Tag = layer;

      m_SpriteProject.SpriteLayers.Add( layer );

      return item;
    }



    private ArrangedItemEntry listLayerSprites_CloningItem( object sender, ArrangedItemEntry Item )
    {
      var origSprite = (SpriteProject.LayerSprite)Item.Tag;

      Formats.SpriteProject.LayerSprite sprite = new Formats.SpriteProject.LayerSprite();
      sprite.X = origSprite.X;
      sprite.Y = origSprite.Y;
      sprite.Index = origSprite.Index;
      sprite.Color = origSprite.Color;
      sprite.ExpandX = origSprite.ExpandX;
      sprite.ExpandY = origSprite.ExpandY;

      m_CurrentLayer.Sprites.Add( sprite );

      ArrangedItemEntry item = new ArrangedItemEntry();
      item.Text = sprite.Index.ToString() + ", " + sprite.X.ToString() + ", " + sprite.Y.ToString();
      item.Tag = sprite;

      return item;
    }



    private void editLayerDelay_TextChanged( object sender, EventArgs e )
    {
      if ( m_CurrentLayer != null )
      {
        if ( m_CurrentLayer.DelayMS != GR.Convert.ToI32( editLayerDelay.Text ) )
        {
          m_CurrentLayer.DelayMS = GR.Convert.ToI32( editLayerDelay.Text );
          SetModified();
        }
      }
    }



    private void checkAutoplayAnim_CheckedChanged( object sender, EventArgs e )
    {
      m_AnimTimer.Enabled = checkAutoplayAnim.Checked;
      if ( m_AnimTimer.Enabled )
      {
        m_AnimTimer.Interval = 100;
        m_AnimTimer.Start();
      }
      else
      {
        m_AnimTimer.Stop();
      }
    }



    private void btnSavePreviewToGIF_Click( object sender, EventArgs e )
    {
      System.Windows.Forms.SaveFileDialog saveDlg = new System.Windows.Forms.SaveFileDialog();

      saveDlg.Title = "Save Preview as GIF";
      saveDlg.Filter = "GIF Image|*.gif";
      if ( DocumentInfo.Project != null )
      {
        saveDlg.InitialDirectory = DocumentInfo.Project.Settings.BasePath;
      }
      if ( saveDlg.ShowDialog() != System.Windows.Forms.DialogResult.OK )
      {
        return;
      }

      // determine bounds
      int     minX = 64000;
      int     maxX = 0;
      int     minY = 48000;
      int     maxY = 0;
      foreach ( var layer in m_SpriteProject.SpriteLayers )
      {
        foreach ( var entry in layer.Sprites )
        {
          minX = Math.Min( entry.X, minX );
          if ( entry.ExpandX )
          {
            maxX = Math.Max( entry.X + 48, minX );
          }
          else
          {
            maxX = Math.Max( entry.X + 24, minX );
          }
          minY = Math.Min( entry.Y, minY );
          if ( entry.ExpandY )
          {
            maxY = Math.Max( entry.Y + 24, minY );
          }
          else
          {
            maxY = Math.Max( entry.Y + 21, minY );
          }
        }
      }

      try
      {
        using ( var outStream = new System.IO.FileStream( saveDlg.FileName, System.IO.FileMode.Create, System.IO.FileAccess.Write ) )
        using ( var gif = new GIFEncoder( outStream, maxX - minX, maxY - minY ) )
        {
          var layerImage = new GR.Image.MemoryImage( maxX - minX, maxY - minY, System.Drawing.Imaging.PixelFormat.Format8bppIndexed );
          CustomRenderer.PaletteManager.ApplyPalette( layerImage );

          foreach ( var layer in m_SpriteProject.SpriteLayers )
          {
            foreach ( var entry in layer.Sprites )
            {
              if ( m_SpriteProject.Sprites[entry.Index].Multicolor )
              {
                SpriteDisplayer.DisplayMultiColorSprite( m_SpriteProject.Sprites[entry.Index].Data,
                                                         layer.BackgroundColor,
                                                         m_SpriteProject.MultiColor1,
                                                         m_SpriteProject.MultiColor2,
                                                         entry.Color,
                                                         layerImage,
                                                         entry.X,
                                                         entry.Y,
                                                         entry.ExpandX,
                                                         entry.ExpandY );
              }
              else
              {
                SpriteDisplayer.DisplayHiResSprite( m_SpriteProject.Sprites[entry.Index].Data,
                                                    layer.BackgroundColor,
                                                    entry.Color,
                                                    layerImage,
                                                    entry.X,
                                                    entry.Y,
                                                    entry.ExpandX,
                                                    entry.ExpandY );
              }
            }

            gif.AddFrame( layerImage.GetAsBitmap(), minX, minY, new TimeSpan( 0, 0, 0, 0, layer.DelayMS ) );
          }
          gif.Close();
        }
      }
      catch ( Exception ex )
      {
        MessageBox.Show( "An exception occurred during saving:\r\n" + ex.ToString(), "Error saving GIF file" );
      }
    }



  }
}
