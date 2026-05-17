using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DicionarioApp
{
    public partial class MainWindow : Window
    {
        // Serviço e estado
        private readonly DicionarioServico _servico = new();
        private Palavra? _palavraSelecionada; // para edição via formulário
        private Palavra? _palavraNoDetalhe;   // palavra exibida no painel de detalhe
        private bool _editando;

        // Construtor
        public MainWindow()
        {
            InitializeComponent();
            DefinirIconeJanela();
            _servico.Carregar();
            AtualizarLista();
        }

        // P/Invoke para liberar o HICON após uso
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool DestroyIcon(IntPtr handle);

        // Ícone da janela / taskbar (desenhado via WPF puro)
        private void DefinirIconeJanela()
        {
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                var violeta = new SolidColorBrush(Color.FromRgb(0x7c, 0x3a, 0xed));
                dc.DrawRoundedRectangle(violeta, null, new Rect(0, 0, 64, 64), 14, 14);

                var branco90 = new SolidColorBrush(Color.FromArgb(230, 255, 255, 255));
                var geomEsq = Geometry.Parse(
                    "M 8,14 C 14,13 22,12.5 30,14 L 30,50 C 22,48.5 14,47.5 8,49 Z");
                dc.DrawGeometry(branco90, null, geomEsq);

                var branco55 = new SolidColorBrush(Color.FromArgb(140, 255, 255, 255));
                var geomDir = Geometry.Parse(
                    "M 34,14 C 42,12.5 50,13 56,14 L 56,49 C 50,47.5 42,48.5 34,50 Z");
                dc.DrawGeometry(branco55, null, geomDir);

                var lombada = new SolidColorBrush(Color.FromArgb(160, 233, 213, 255));
                dc.DrawRectangle(lombada, null, new Rect(29, 13, 6, 37));

                var linhaEsq = new Pen(new SolidColorBrush(Color.FromArgb(100, 255, 255, 255)), 1.8);
                dc.DrawLine(linhaEsq, new Point(12, 24), new Point(26, 24));
                dc.DrawLine(linhaEsq, new Point(12, 30), new Point(26, 30));
                dc.DrawLine(linhaEsq, new Point(12, 36), new Point(24, 36));

                var linhaDir = new Pen(new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)), 1.8);
                dc.DrawLine(linhaDir, new Point(38, 24), new Point(52, 24));
                dc.DrawLine(linhaDir, new Point(38, 30), new Point(52, 30));
                dc.DrawLine(linhaDir, new Point(38, 36), new Point(50, 36));

                var estrela = new SolidColorBrush(Color.FromArgb(220, 233, 213, 255));
                dc.DrawEllipse(estrela, null, new Point(32, 7), 3.5, 3.5);
            }

            var rtb = new RenderTargetBitmap(64, 64, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            using var ms = new System.IO.MemoryStream();
            encoder.Save(ms);
            ms.Position = 0;

            using var bmp = new System.Drawing.Bitmap(ms);
            var hIcon = bmp.GetHicon();
            try
            {
                Icon = Imaging.CreateBitmapSourceFromHIcon(
                    hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            }
            finally
            {
                DestroyIcon(hIcon);
            }
        }

        // SCROLL — repassa o scroll do ListBox para o ScrollViewer pai
        private void ListaPalavras_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            scrollLista.ScrollToVerticalOffset(scrollLista.VerticalOffset - e.Delta / 3.0);
            e.Handled = true;
        }

        // FORMULÁRIO

        private void Adicionar_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtPalavra.Text) ||
                string.IsNullOrWhiteSpace(txtSignificado.Text))
            {
                MessageBox.Show("Preencha a palavra e o significado.",
                    "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                if (_editando && _palavraSelecionada != null)
                {
                    _palavraSelecionada.PalavraTexto = txtPalavra.Text.Trim();
                    _palavraSelecionada.Significado  = txtSignificado.Text.Trim();
                    _palavraSelecionada.Exemplo      = txtExemplo.Text.Trim();
                    _servico.Salvar();
                    MessageBox.Show("Palavra atualizada com sucesso!",
                        "Sucesso", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    _servico.Adicionar(
                        txtPalavra.Text.Trim(),
                        txtSignificado.Text.Trim(),
                        txtExemplo.Text.Trim());
                    MessageBox.Show("Palavra adicionada com sucesso!",
                        "Sucesso", MessageBoxButton.OK, MessageBoxImage.Information);
                }

                LimparFormulario();
                AtualizarLista();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao salvar palavra: {ex.Message}",
                    "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Limpar_Click(object sender, RoutedEventArgs e) => LimparFormulario();

        private void Buscar_TextChanged(object sender, TextChangedEventArgs e)
            => AtualizarLista(txtBusca.Text.Trim());

        // LISTA DE PALAVRAS

        private void ItemCard_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (OrigemEhBotao(e.OriginalSource as DependencyObject))
                return;

            if (sender is Border border && border.DataContext is Palavra p)
                MostrarDetalhe(p);
        }

        private void EditarItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.CommandParameter is Palavra p)
                CarregarNoFormulario(p);
        }

        private void RemoverItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.CommandParameter is Palavra p)
                RemoverPalavra(p);
        }

        // PAINEL DE DETALHE

        /// <summary>
        /// Exibe o painel de detalhe para a palavra escolhida.
        /// Os exemplos são separados por 2+ espaços e exibidos individualmente.
        /// </summary>
        private void MostrarDetalhe(Palavra p)
        {
            _palavraNoDetalhe = p;

            lblDetalhePalavra.Text    = p.PalavraTexto;
            lblDetalheSignificado.Text = p.Significado;

            // ── MODIFICAÇÃO: separa os exemplos e popula o ItemsControl ──
            var exemplos = SepararExemplos(p.Exemplo);
            listaExemplos.ItemsSource = exemplos;

            boxExemplo.Visibility = exemplos.Count > 0
                ? Visibility.Visible
                : Visibility.Collapsed;

            scrollLista.Visibility  = Visibility.Collapsed;
            scrollDetalhe.Visibility = Visibility.Visible;
            scrollDetalhe.ScrollToTop();
        }

        /// <summary>
        /// Separa uma string de exemplos em itens individuais.
        /// O separador são 2 ou mais espaços consecutivos, que é o padrão
        /// usado ao cadastrar os exemplos no campo de texto.
        /// </summary>
        private static List<string> SepararExemplos(string exemplo)
        {
            if (string.IsNullOrWhiteSpace(exemplo))
                return new List<string>();

            return Regex.Split(exemplo.Trim(), @"\s{2,}")
                        .Where(e => !string.IsNullOrWhiteSpace(e))
                        .Select(e => e.Trim())
                        .ToList();
        }

        private void Voltar_Click(object sender, RoutedEventArgs e)
        {
            scrollDetalhe.Visibility = Visibility.Collapsed;
            scrollLista.Visibility   = Visibility.Visible;
            _palavraNoDetalhe = null;
        }

        private void EditarDetalhe_Click(object sender, RoutedEventArgs e)
        {
            if (_palavraNoDetalhe == null) return;
            CarregarNoFormulario(_palavraNoDetalhe);
            Voltar_Click(sender, e);
        }

        private void RemoverDetalhe_Click(object sender, RoutedEventArgs e)
        {
            if (_palavraNoDetalhe == null) return;
            var p = _palavraNoDetalhe;
            if (RemoverPalavra(p))
            {
                Voltar_Click(sender, e);
                AtualizarLista(txtBusca.Text.Trim());
            }
        }

        private void CarregarNoFormulario(Palavra p)
        {
            _palavraSelecionada = p;
            _editando           = true;
            txtPalavra.Text     = p.PalavraTexto;
            txtSignificado.Text = p.Significado;
            txtExemplo.Text     = p.Exemplo;
            btnAdicionar.Content = "Salvar";
            txtPalavra.Focus();
        }

        private bool RemoverPalavra(Palavra p)
        {
            var res = MessageBox.Show(
                $"Tem certeza que quer remover \"{p.PalavraTexto}\"?",
                "Confirmação", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (res != MessageBoxResult.Yes) return false;

            _servico.Deletar(p.PalavraTexto);
            AtualizarLista(txtBusca.Text.Trim());
            return true;
        }

        private void LimparFormulario()
        {
            txtPalavra.Clear();
            txtSignificado.Clear();
            txtExemplo.Clear();
            txtBusca.Clear();
            _palavraSelecionada  = null;
            _editando            = false;
            btnAdicionar.Content = "Adicionar";
            listaPalavras.SelectedItem = null;
            AtualizarLista();
        }

        private void AtualizarLista(string filtro = "")
        {
            var palavras = _servico.Buscar(filtro);
            listaPalavras.ItemsSource = palavras;
            lblTotal.Text = $"{palavras.Count} palavras";
        }

        private static bool OrigemEhBotao(DependencyObject? elemento)
        {
            var atual = elemento;
            while (atual != null)
            {
                if (atual is Button) return true;
                atual = VisualTreeHelper.GetParent(atual);
            }
            return false;
        }
    }
}
